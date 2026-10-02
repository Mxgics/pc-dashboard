using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PcDashboard.Core;

public sealed class BackupService
{
    public const int ArchiveVersion = 1;
    public const int Iterations = 600_000;
    public const int MaximumArchiveBytes = 10 * 1024 * 1024;
    public const int MaximumPlaintextBytes = 5 * 1024 * 1024;
    private const int SaltLength = 16;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private static readonly byte[] Magic = "PCDBAK01"u8.ToArray();
    private readonly IBackupStore store;
    private readonly IActionStore actions;
    private readonly OperationCoordinator operations;
    private readonly TimeProvider timeProvider;
    private readonly string safetyDirectory;

    public BackupService(IBackupStore store, IActionStore actions,
        OperationCoordinator operations, TimeProvider timeProvider, string safetyDirectory)
    {
        this.store = store;
        this.actions = actions;
        this.operations = operations;
        this.timeProvider = timeProvider;
        this.safetyDirectory = Path.GetFullPath(safetyDirectory);
    }

    public async Task CreateAsync(string archivePath, string passphrase,
        CancellationToken ct = default)
    {
        ValidateArchivePath(archivePath);
        ValidatePassphrase(passphrase);
        await using var lease = await operations.TryBeginAsync(ActionKind.Backup, ct)
            ?? throw new InvalidOperationException("Another refresh or maintenance operation is already running.");
        var payload = await store.ExportBackupAsync(ct);
        var bytes = Encrypt(payload, passphrase);
        var actionId = Guid.NewGuid();
        var subject = Path.GetFileName(archivePath);
        await actions.StartActionAsync(new(actionId, ActionKind.Backup, subject,
            ActionStatus.Running, timeProvider.GetUtcNow()), ct);
        try
        {
            await WriteAtomicAsync(archivePath, bytes, overwrite: true, ct);
            await actions.CompleteActionAsync(actionId, new(ActionStatus.Succeeded, subject,
                "Authenticated encrypted backup created.", CompletedAt: timeProvider.GetUtcNow()),
                CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            await actions.CompleteActionAsync(actionId, new(ActionStatus.Cancelled, subject,
                "Backup creation cancelled.", CompletedAt: timeProvider.GetUtcNow()), CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            await actions.CompleteActionAsync(actionId, new(ActionStatus.Failed, subject,
                $"Backup creation failed: {ex.Message}", CompletedAt: timeProvider.GetUtcNow()), CancellationToken.None);
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public async Task<BackupPreview> PreviewAsync(string archivePath, string passphrase,
        CancellationToken ct = default)
    {
        ValidateArchivePath(archivePath);
        ValidatePassphrase(passphrase);
        var archive = await ReadArchiveAsync(archivePath, ct);
        try
        {
            var payload = Decrypt(archive, passphrase);
            ValidatePayload(payload);
            return new(Path.GetFullPath(archivePath), Convert.ToHexString(SHA256.HashData(archive)),
                payload.ExportedAt, payload.Categories.Count, payload.Aliases.Count,
                payload.Assignments.Count, payload.Profiles.Count,
                payload.Profiles.Sum(profile => profile.Items.Count));
        }
        finally { CryptographicOperations.ZeroMemory(archive); }
    }

    public async Task<RestoreResult> RestoreAsync(BackupPreview preview, string passphrase,
        CancellationToken ct = default)
    {
        ValidatePassphrase(passphrase);
        await using var lease = await operations.TryBeginAsync(ActionKind.Restore, ct)
            ?? throw new InvalidOperationException("Another refresh or maintenance operation is already running.");

        var archive = await ReadArchiveAsync(preview.ArchivePath, ct);
        BackupPayload payload;
        try
        {
            var digest = Convert.ToHexString(SHA256.HashData(archive));
            if (!PlanDigests.Equals(preview.ArchiveDigest, digest))
                throw OpenFailure();
            payload = Decrypt(archive, passphrase);
            ValidatePayload(payload);
        }
        finally { CryptographicOperations.ZeroMemory(archive); }

        var current = await store.ExportBackupAsync(ct);
        Directory.CreateDirectory(safetyDirectory);
        var safetyPath = Path.Combine(safetyDirectory,
            $"safety-before-restore-{timeProvider.GetUtcNow():yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.pcdbak");
        var safetyBytes = Encrypt(current, passphrase);
        try { await WriteAtomicAsync(safetyPath, safetyBytes, overwrite: false, ct); }
        finally { CryptographicOperations.ZeroMemory(safetyBytes); }

        var actionId = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();
        await actions.StartActionAsync(new(actionId, ActionKind.Restore,
            Path.GetFileName(preview.ArchivePath), ActionStatus.Running, now,
            Summary: "Encrypted safety backup created before restore."), CancellationToken.None);
        try
        {
            await store.RestoreBackupAsync(payload, ct);
            var result = new ActionResult(ActionStatus.Succeeded,
                Path.GetFileName(preview.ArchivePath),
                "Backup restored in one database transaction.", CompletedAt: timeProvider.GetUtcNow());
            await actions.CompleteActionAsync(actionId, result, CancellationToken.None);
            return new(ActionStatus.Succeeded, result.Message, safetyPath);
        }
        catch (OperationCanceledException)
        {
            var result = new ActionResult(ActionStatus.Cancelled,
                Path.GetFileName(preview.ArchivePath),
                "Restore cancelled before the database transaction committed.",
                CompletedAt: timeProvider.GetUtcNow());
            await actions.CompleteActionAsync(actionId, result, CancellationToken.None);
            return new(ActionStatus.Cancelled, result.Message, safetyPath);
        }
        catch (Exception ex)
        {
            var result = new ActionResult(ActionStatus.Failed,
                Path.GetFileName(preview.ArchivePath),
                $"Restore failed and the database transaction was rolled back: {ex.Message}",
                CompletedAt: timeProvider.GetUtcNow());
            await actions.CompleteActionAsync(actionId, result, CancellationToken.None);
            return new(ActionStatus.Failed, result.Message, safetyPath);
        }
    }

    private static byte[] Encrypt(BackupPayload payload, string passphrase)
    {
        ValidatePayload(payload);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload);
        if (plaintext.Length > MaximumPlaintextBytes)
            throw new InvalidOperationException("Backup content exceeds the supported size limit.");
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Iterations,
            HashAlgorithmName.SHA256, 32);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        var header = Header(salt, nonce, ciphertext.Length);
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, header);
            var archive = new byte[header.Length + TagLength + ciphertext.Length];
            header.CopyTo(archive, 0);
            tag.CopyTo(archive, header.Length);
            ciphertext.CopyTo(archive, header.Length + TagLength);
            return archive;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(ciphertext);
        }
    }

    private static BackupPayload Decrypt(byte[] archive, string passphrase)
    {
        try
        {
            var headerLength = Magic.Length + 4 + 4 + SaltLength + NonceLength + 4;
            if (archive.Length < headerLength + TagLength || archive.Length > MaximumArchiveBytes
                || !archive.AsSpan(0, Magic.Length).SequenceEqual(Magic))
                throw OpenFailure();
            var version = BinaryPrimitives.ReadInt32LittleEndian(archive.AsSpan(Magic.Length, 4));
            if (version != ArchiveVersion)
                throw new InvalidOperationException("This backup version is not supported.");
            var iterations = BinaryPrimitives.ReadInt32LittleEndian(archive.AsSpan(Magic.Length + 4, 4));
            if (iterations != Iterations) throw OpenFailure();
            var saltOffset = Magic.Length + 8;
            var nonceOffset = saltOffset + SaltLength;
            var lengthOffset = nonceOffset + NonceLength;
            var ciphertextLength = BinaryPrimitives.ReadInt32LittleEndian(archive.AsSpan(lengthOffset, 4));
            if (ciphertextLength < 0 || ciphertextLength > MaximumPlaintextBytes
                || archive.Length != headerLength + TagLength + ciphertextLength)
                throw OpenFailure();
            var salt = archive.AsSpan(saltOffset, SaltLength);
            var nonce = archive.AsSpan(nonceOffset, NonceLength);
            var tag = archive.AsSpan(headerLength, TagLength);
            var ciphertext = archive.AsSpan(headerLength + TagLength, ciphertextLength);
            var plaintext = new byte[ciphertextLength];
            var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Iterations,
                HashAlgorithmName.SHA256, 32);
            try
            {
                using var aes = new AesGcm(key, TagLength);
                aes.Decrypt(nonce, ciphertext, tag, plaintext, archive.AsSpan(0, headerLength));
                return JsonSerializer.Deserialize<BackupPayload>(plaintext) ?? throw OpenFailure();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException)
        {
            throw OpenFailure();
        }
    }

    private static byte[] Header(byte[] salt, byte[] nonce, int ciphertextLength)
    {
        var header = new byte[Magic.Length + 4 + 4 + SaltLength + NonceLength + 4];
        Magic.CopyTo(header, 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(Magic.Length, 4), ArchiveVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(Magic.Length + 4, 4), Iterations);
        salt.CopyTo(header, Magic.Length + 8);
        nonce.CopyTo(header, Magic.Length + 8 + SaltLength);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(header.Length - 4, 4), ciphertextLength);
        return header;
    }

    private static async Task<byte[]> ReadArchiveAsync(string path, CancellationToken ct)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw OpenFailure();
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
            FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaximumArchiveBytes)
            throw OpenFailure();
        var bytes = new byte[(int)stream.Length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(offset), ct);
            if (read == 0) break;
            offset += read;
        }
        if (offset != bytes.Length) Array.Resize(ref bytes, offset);
        return bytes;
    }

    private static async Task WriteAtomicAsync(string path, byte[] bytes, bool overwrite,
        CancellationToken ct)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("Choose a backup file in an existing folder.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, ct);
                await stream.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, fullPath, overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static void ValidatePayload(BackupPayload payload)
    {
        if (payload.SchemaVersion != ArchiveVersion)
            throw new InvalidOperationException("This backup data schema is not supported.");
        if (payload.Categories is null || payload.Aliases is null
            || payload.Assignments is null || payload.Profiles is null)
            throw new InvalidOperationException("Backup content is invalid.");
        if (payload.Categories.Count > 500 || payload.Aliases.Count > 1_000
            || payload.Assignments.Count > 100_000 || payload.Profiles.Count > 500)
            throw new InvalidOperationException("Backup content exceeds supported record limits.");
        if (payload.Aliases.Any(alias => alias is null)
            || payload.Assignments.Any(assignment => assignment is null)
            || payload.Profiles.Any(profile => profile is null || profile.Items is null))
            throw new InvalidOperationException("Backup content is invalid.");
        if (payload.Profiles.Sum(profile => profile.Items.Count) > 50_000)
            throw new InvalidOperationException("Backup content exceeds supported record limits.");
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in payload.Categories)
        {
            if (category is null) throw new InvalidOperationException("Backup categories are invalid.");
            if (!categories.Add(Categories.Validate(category)))
                throw new InvalidOperationException("Backup contains duplicate categories.");
        }
        if (payload.Aliases.Any(alias => alias.Original is null || alias.Category is null
                || alias.Original.Length is < 1 or > 40
                || alias.Original.Any(char.IsControl) || !categories.Contains(alias.Category)))
            throw new InvalidOperationException("Backup aliases are invalid.");
        if (payload.Aliases.Select(alias => alias.Original).Distinct(StringComparer.Ordinal).Count()
            != payload.Aliases.Count)
            throw new InvalidOperationException("Backup aliases are invalid.");
        if (payload.Assignments.Any(assignment => assignment.AppId is null || assignment.Category is null
                || assignment.AppId.Length is < 1 or > 500 || assignment.AppId.Any(char.IsControl)
                || !categories.Contains(assignment.Category)))
            throw new InvalidOperationException("Backup assignments are invalid.");
        if (payload.Assignments.Select(assignment => assignment.AppId).Distinct(StringComparer.Ordinal).Count()
            != payload.Assignments.Count)
            throw new InvalidOperationException("Backup assignments are invalid.");
        foreach (var profile in payload.Profiles)
        {
            if (profile is null || profile.Name is null || profile.Items is null)
                throw new InvalidOperationException("Backup setup profiles are invalid.");
            SqliteDashboardStore.ValidateSetupProfile(profile);
        }
        if (payload.Profiles.Select(profile => profile.Id).Distinct().Count() != payload.Profiles.Count
            || payload.Profiles.Select(profile => profile.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != payload.Profiles.Count)
            throw new InvalidOperationException("Backup setup profiles are invalid.");
    }

    private static void ValidateArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !string.Equals(Path.GetExtension(path), ".pcdbak", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a .pcdbak file.");
    }

    private static void ValidatePassphrase(string passphrase)
    {
        if (string.IsNullOrEmpty(passphrase) || passphrase.Length > 1_024)
            throw new ArgumentException("Enter a passphrase of no more than 1,024 characters.");
    }

    private static InvalidOperationException OpenFailure() => new(
        "Backup could not be opened. The passphrase may be wrong or the file may have been changed.");
}
