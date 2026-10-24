using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Shared;

namespace WinClonerX.Domain.Planning;

/// <summary>
/// Plan de clonado (agregado raíz): decisión sobre cómo se clonará el origen en el destino, calculada a partir de un
/// inventario concreto. Solo <see cref="ClonePlanner"/> lo crea; <see cref="Create"/> valida las invariantes.
/// Ciclo de vida: Draft → Confirmed, o Draft/Confirmed → Invalidated.
/// Un plan con <see cref="SourceShrink"/> exige, además, la <see cref="ShrinkConfirmation"/> al confirmarse.
/// </summary>
public sealed class ClonePlan
{
    private readonly HashSet<PreflightCode> _acceptedWarnings = [];

    private ClonePlan(
        PlanId id,
        DiskId source,
        DiskId target,
        InventoryFingerprint fingerprint,
        IReadOnlyList<PartitionMapping> mappings,
        Guid newDiskGuid,
        VerificationMode verification,
        IReadOnlyList<PreflightCheck> warnings,
        CloneEstimate estimate,
        SourceShrinkStep? sourceShrink)
    {
        Id = id;
        Source = source;
        Target = target;
        Fingerprint = fingerprint;
        Mappings = mappings;
        NewDiskGuid = newDiskGuid;
        Verification = verification;
        Warnings = warnings;
        Estimate = estimate;
        SourceShrink = sourceShrink;
    }

    /// <summary>Identificador del plan.</summary>
    public PlanId Id { get; }

    /// <summary>Disco de origen (el del sistema).</summary>
    public DiskId Source { get; }

    /// <summary>Disco de destino, que se borrará por completo al ejecutar el plan.</summary>
    public DiskId Target { get; }

    /// <summary>Huella del inventario con la que se calculó el plan; si cambia, el plan se invalida (FR-011).</summary>
    public InventoryFingerprint Fingerprint { get; }

    /// <summary>Una correspondencia por partición de origen, en su mismo orden (FR-021b).</summary>
    public IReadOnlyList<PartitionMapping> Mappings { get; }

    /// <summary>GUID de disco nuevo del clon (FR-017).</summary>
    public Guid NewDiskGuid { get; }

    /// <summary>Verificación que se hará al terminar.</summary>
    public VerificationMode Verification { get; }

    /// <summary>Avisos del plan, que hay que aceptar antes de confirmar.</summary>
    public IReadOnlyList<PreflightCheck> Warnings { get; }

    /// <summary>Bytes a copiar y duración estimada.</summary>
    public CloneEstimate Estimate { get; }

    /// <summary>
    /// Reducción temporal de la partición de Windows del origen, presente solo cuando el destino de Windows es más pequeño
    /// que el origen (estrategia ShrinkSource). Null en el resto de planes.
    /// </summary>
    public SourceShrinkStep? SourceShrink { get; }

    /// <summary>Situación actual del plan.</summary>
    public PlanStatus Status { get; private set; } = PlanStatus.Draft;

    /// <summary>Códigos de aviso aceptados por la persona.</summary>
    public IReadOnlySet<PreflightCode> AcceptedWarnings => _acceptedWarnings;

    /// <summary>Confirmación del destino, presente solo cuando el plan está confirmado.</summary>
    public TargetConfirmation? Confirmation { get; private set; }

    /// <summary>Confirmación de la reducción del origen, presente solo cuando el plan está confirmado y reduce el origen.</summary>
    public ShrinkConfirmation? SourceShrinkConfirmation { get; private set; }

    /// <summary>Motivo de la invalidación, presente solo cuando el plan está invalidado.</summary>
    public string? InvalidationReason { get; private set; }

    /// <summary>
    /// Crea el plan validando sus invariantes. Lanza <see cref="InvalidOperationException"/> si alguna no se cumple,
    /// lo que indica un fallo del planificador y no un error de la persona. <paramref name="sourceShrink"/> es la reducción
    /// de la partición de Windows del origen, o null si el plan no la necesita.
    /// </summary>
    internal static ClonePlan Create(
        PlanId id,
        Disk source,
        Disk target,
        InventoryFingerprint fingerprint,
        IReadOnlyList<PartitionMapping> mappings,
        Guid newDiskGuid,
        VerificationMode verification,
        IReadOnlyList<PreflightCheck> warnings,
        CloneEstimate estimate,
        CapacityPolicy policy,
        SourceShrinkStep? sourceShrink = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(warnings);
        ArgumentNullException.ThrowIfNull(policy);

        ValidateTargetIdentity(source, target);
        ValidateSectorSize(source, target);
        ValidateLayout(source, target, mappings);
        ValidateOnlyWindowsResizes(source, mappings, sourceShrink);
        ValidateSourceShrink(source, mappings, sourceShrink, policy);
        ValidateCapacity(source, mappings, policy);
        ValidateNewGuids(source, newDiskGuid, mappings);

        return new ClonePlan(id, source.Id, target.Id, fingerprint, mappings, newDiskGuid, verification, warnings, estimate, sourceShrink);
    }

    /// <summary>Acepta los avisos indicados. Solo en borrador, y solo códigos que el plan tiene como aviso.</summary>
    public void AcceptWarnings(IEnumerable<PreflightCode> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        EnsureDraft("Solo se pueden aceptar avisos mientras el plan está en borrador.");

        var requested = codes.ToArray();
        foreach (var code in requested)
        {
            if (!Warnings.Any(warning => warning.Code == code))
            {
                throw new ArgumentException($"El plan no tiene el aviso {code}.", nameof(codes));
            }
        }

        foreach (var code in requested)
        {
            _acceptedWarnings.Add(code);
        }
    }

    /// <summary>
    /// Confirma el plan. Exige borrador, todos los avisos aceptados, el disco de destino del plan y una confirmación
    /// que coincida con él. Si el plan reduce el origen, exige además <paramref name="shrink"/> con la misma partición y
    /// longitud reducida (FR-013); si no reduce el origen, no admite ninguna. Si no se cumple, lanza
    /// <see cref="InvalidOperationException"/> y el plan no cambia.
    /// </summary>
    public void Confirm(TargetConfirmation confirmation, Disk targetDisk, ShrinkConfirmation? shrink = null)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        ArgumentNullException.ThrowIfNull(targetDisk);
        EnsureDraft("Solo se puede confirmar un plan en borrador.");

        if (targetDisk.Id != Target)
        {
            throw new InvalidOperationException("El disco indicado no es el destino de este plan.");
        }

        var pending = Warnings.Select(warning => warning.Code).Where(code => !_acceptedWarnings.Contains(code)).ToArray();
        if (pending.Length > 0)
        {
            throw new InvalidOperationException("Hay avisos sin aceptar: " + string.Join(", ", pending) + ".");
        }

        if (!confirmation.Matches(targetDisk))
        {
            throw new InvalidOperationException("La confirmación no coincide con el disco de destino del plan.");
        }

        ValidateShrinkConfirmation(shrink);

        Confirmation = confirmation;
        SourceShrinkConfirmation = shrink;
        Status = PlanStatus.Confirmed;
    }

    /// <summary>Invalida el plan porque cambió el estado de los discos. Es definitivo: hay que volver a planificar.</summary>
    public void Invalidate(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("El motivo de la invalidación no puede estar vacío.", nameof(reason));
        }

        if (Status == PlanStatus.Invalidated)
        {
            throw new InvalidOperationException("El plan ya está invalidado.");
        }

        Status = PlanStatus.Invalidated;
        InvalidationReason = reason;
    }

    /// <summary>
    /// Construye el diseño que se escribirá en el destino: el GUID de disco nuevo y, por cada correspondencia, el tipo,
    /// los atributos y el nombre de la partición de origen con su identificador y extensión nuevos.
    /// Lanza <see cref="InvalidOperationException"/> si el disco no es el destino del plan.
    /// </summary>
    public TargetLayout ToTargetLayout(Disk targetDisk)
    {
        ArgumentNullException.ThrowIfNull(targetDisk);

        if (targetDisk.Id != Target)
        {
            throw new InvalidOperationException("El disco indicado no es el destino de este plan.");
        }

        var entries = Mappings
            .Select(mapping => new TargetLayoutEntry(
                mapping.Source.Type,
                mapping.NewUniqueId,
                mapping.TargetExtent,
                mapping.Source.Attributes,
                mapping.Source.Name))
            .ToArray();

        return new TargetLayout(NewDiskGuid, targetDisk.Size, targetDisk.LogicalSectorSize, entries);
    }

    private void EnsureDraft(string message)
    {
        if (Status != PlanStatus.Draft)
        {
            throw new InvalidOperationException(message);
        }
    }

    // La confirmación de reducción debe existir y coincidir si el plan reduce el origen; si no lo reduce, no debe haberla.
    private void ValidateShrinkConfirmation(ShrinkConfirmation? shrink)
    {
        if (SourceShrink is not { } expected)
        {
            if (shrink is not null)
            {
                throw new InvalidOperationException("El plan no reduce el origen: no admite confirmación de reducción.");
            }

            return;
        }

        if (shrink is null)
        {
            throw new InvalidOperationException("El plan reduce la partición de Windows del origen: hace falta la confirmación de la reducción (FR-013).");
        }

        if (shrink.Partition != expected.Partition || shrink.ShrunkLength != expected.ShrunkLength)
        {
            throw new InvalidOperationException("La confirmación de la reducción no coincide con la del plan.");
        }
    }

    // Invariante 1: el destino no es el origen ni el disco del sistema (FR-004).
    private static void ValidateTargetIdentity(Disk source, Disk target)
    {
        if (target.Id == source.Id)
        {
            throw Invariant(1, "el destino no puede ser el disco de origen.");
        }

        if (target.IsSystem)
        {
            throw Invariant(1, "el destino no puede ser el disco del sistema.");
        }
    }

    // Invariante 2: el mismo tamaño de sector lógico.
    private static void ValidateSectorSize(Disk source, Disk target)
    {
        if (target.LogicalSectorSize != source.LogicalSectorSize)
        {
            throw Invariant(2, "el tamaño de sector lógico del destino debe coincidir con el del origen.");
        }
    }

    // Invariante 3: una correspondencia por partición, en orden, sin solapes, alineadas a 1 MiB y dentro del área utilizable.
    private static void ValidateLayout(Disk source, Disk target, IReadOnlyList<PartitionMapping> mappings)
    {
        if (mappings.Count != source.Partitions.Count)
        {
            throw Invariant(3, "hay que asignar exactamente una correspondencia por partición de origen.");
        }

        var firstUsable = TargetLayoutCalculator.FirstUsableByte(target.LogicalSectorSize);
        var lastUsable = TargetLayoutCalculator.LastUsableByte(target);
        for (var i = 0; i < mappings.Count; i++)
        {
            var extent = mappings[i].TargetExtent;

            if (!mappings[i].Source.Equals(source.Partitions[i]))
            {
                throw Invariant(3, "las correspondencias deben seguir el orden de las particiones de origen.");
            }

            if (extent.Offset % TargetLayoutCalculator.Alignment != 0)
            {
                throw Invariant(3, "cada extensión de destino debe estar alineada a 1 MiB.");
            }

            if (extent.Offset < firstUsable || extent.End > lastUsable)
            {
                throw Invariant(3, "cada extensión de destino debe quedar dentro del área utilizable de la GPT.");
            }

            if (i > 0 && extent.Offset < mappings[i - 1].TargetExtent.End)
            {
                throw Invariant(3, "las extensiones de destino no pueden solaparse.");
            }
        }
    }

    // Invariante 4: solo la partición de Windows puede cambiar de longitud. Crecer exige ampliar su sistema de archivos.
    // Reducirla solo es posible con SourceShrink; su coherencia se comprueba en la invariante 5.
    private static void ValidateOnlyWindowsResizes(Disk source, IReadOnlyList<PartitionMapping> mappings, SourceShrinkStep? sourceShrink)
    {
        for (var i = 0; i < mappings.Count; i++)
        {
            var sourcePartition = source.Partitions[i];
            var targetLength = mappings[i].TargetExtent.Length;
            var sourceLength = sourcePartition.Extent.Length;

            if (targetLength == sourceLength)
            {
                if (mappings[i].Resize != ResizeKind.None)
                {
                    throw Invariant(4, "solo la partición de Windows puede tener una ampliación del sistema de archivos.");
                }

                continue;
            }

            if (sourcePartition.Role != PartitionRole.Windows)
            {
                throw Invariant(4, "solo la partición de Windows puede cambiar de longitud.");
            }

            if (targetLength < sourceLength)
            {
                if (sourceShrink is null)
                {
                    throw Invariant(4, "la partición de Windows solo puede reducirse con una reducción del origen (SourceShrink).");
                }

                // Con reducción, el sistema de archivos crece desde la longitud reducida: lo comprueba la invariante 5.
                continue;
            }

            if (mappings[i].Resize != ResizeKind.GrowFileSystem)
            {
                throw Invariant(4, "la partición de Windows que crece debe ampliar su sistema de archivos.");
            }
        }
    }

    // Invariante 5: la reducción del origen es coherente con el destino de Windows. Solo se exige con reducción de Windows
    // en el destino (la ausencia de SourceShrink en ese caso la rechaza la invariante 4).
    // La reducción refiere a la partición de Windows del origen, su longitud original es la actual, el destino de Windows es
    // más pequeño que el origen, la longitud reducida cabe en el destino y cubre lo ocupado más el margen, y el sistema de
    // archivos crece desde la longitud reducida solo si queda espacio.
    private static void ValidateSourceShrink(Disk source, IReadOnlyList<PartitionMapping> mappings, SourceShrinkStep? shrink, CapacityPolicy policy)
    {
        if (shrink is null)
        {
            return;
        }

        var windowsIndex = Enumerable.Range(0, source.Partitions.Count).Single(i => source.Partitions[i].Role == PartitionRole.Windows);
        var windows = source.Partitions[windowsIndex];
        var targetLength = mappings[windowsIndex].TargetExtent.Length;

        if (shrink.Partition != windows.UniqueId)
        {
            throw Invariant(5, "la reducción del origen debe referirse a la partición de Windows.");
        }

        if (shrink.OriginalLength.Bytes != windows.Extent.Length)
        {
            throw Invariant(5, "la longitud original de la reducción debe ser la longitud actual de la partición de Windows.");
        }

        if (targetLength >= windows.Extent.Length)
        {
            throw Invariant(5, "una reducción del origen solo tiene sentido si la partición de Windows del destino es más pequeña.");
        }

        if (shrink.ShrunkLength.Bytes > targetLength)
        {
            throw Invariant(5, "la longitud reducida no puede superar la partición de Windows del destino.");
        }

        var used = windows.Volume?.Used
            ?? throw Invariant(5, "la partición de Windows no tiene volumen.");
        if (shrink.ShrunkLength < policy.Required(used))
        {
            throw Invariant(5, "la longitud reducida debe dejar lo ocupado más el margen.");
        }

        var expectedResize = targetLength > shrink.ShrunkLength.Bytes ? ResizeKind.GrowFileSystem : ResizeKind.None;
        if (mappings[windowsIndex].Resize != expectedResize)
        {
            throw Invariant(5, "el sistema de archivos de Windows debe crecer desde la longitud reducida hasta el destino.");
        }
    }

    // Invariante 6: lo ocupado más el margen cabe en la capacidad asignada a Windows.
    private static void ValidateCapacity(Disk source, IReadOnlyList<PartitionMapping> mappings, CapacityPolicy policy)
    {
        var windowsIndex = Enumerable.Range(0, source.Partitions.Count).Single(i => source.Partitions[i].Role == PartitionRole.Windows);
        var windowsUsed = source.Partitions[windowsIndex].Volume?.Used
            ?? throw Invariant(6, "la partición de Windows no tiene volumen.");
        var required = policy.Required(windowsUsed);

        if (mappings[windowsIndex].TargetExtent.Length < required.Bytes)
        {
            throw Invariant(6, $"la partición de Windows de destino ({mappings[windowsIndex].TargetExtent.Length} bytes) " +
                $"no alcanza para los datos ocupados y el margen ({required.Bytes} bytes).");
        }
    }

    // Invariante 7: GUID nuevos, únicos y distintos de los del origen.
    private static void ValidateNewGuids(Disk source, Guid newDiskGuid, IReadOnlyList<PartitionMapping> mappings)
    {
        if (newDiskGuid == Guid.Empty)
        {
            throw Invariant(7, "el GUID de disco nuevo no puede estar vacío.");
        }

        var sourceGuids = source.Partitions.Select(partition => partition.UniqueId.Value).Append(source.Id.DiskGuid).ToHashSet();
        var newGuids = new HashSet<Guid> { newDiskGuid };
        if (sourceGuids.Contains(newDiskGuid))
        {
            throw Invariant(7, "el GUID de disco nuevo no puede ser el del origen.");
        }

        foreach (var mapping in mappings)
        {
            var guid = mapping.NewUniqueId.Value;
            if (sourceGuids.Contains(guid))
            {
                throw Invariant(7, "los GUID de partición nuevos no pueden ser los del origen.");
            }

            if (!newGuids.Add(guid))
            {
                throw Invariant(7, "los GUID nuevos (disco y particiones) deben ser únicos.");
            }
        }
    }

    private static InvalidOperationException Invariant(int number, string message) =>
        new($"Invariante {number}: {message}");
}
