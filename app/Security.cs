using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Clinic06;

internal sealed record Session(uint Id, string FullName, string Role, uint? DoctorId)
{
    public void Require(string role)
    {
        if (Role != role)
            throw new InvalidOperationException("Для этой операции требуется другая роль.");
    }
}

internal static class Security
{
    public const string Admin = "admin";
    public const string Registrar = "registrar";
    public const string Doctor = "doctor";
    public const int Iterations = 600_000;

    public static (byte[] Hash, byte[] Salt) CreateDigest(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        return (Derive(password, salt, Iterations), salt);
    }

    public static bool Verify(string password, byte[] hash, byte[] salt, int iterations) =>
        CryptographicOperations.FixedTimeEquals(hash, Derive(password, salt, iterations));

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);

    public static string NormalizeLogin(string login) => login.Trim().ToLowerInvariant();

    public static bool ValidLogin(string login) => Regex.IsMatch(login, @"\A[a-z0-9_.-]{4,32}\z");
}
