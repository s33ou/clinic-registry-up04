namespace Clinic06;

// учебный шифр для телефонов вымышленных пациентов
internal static class DemoPhoneCipher
{
    private const int Shift = 3;

    public static string Encrypt(string phone) => Rotate(phone, Shift);

    public static string Decrypt(string cipher) => Rotate(cipher, 10 - Shift);

    private static string Rotate(string source, int offset) =>
        string.Create(source.Length, (Source: source, Offset: offset), (result, state) =>
        {
            for (int i = 0; i < result.Length; i++)
            {
                char value = state.Source[i];
                result[i] = char.IsAsciiDigit(value)
                    ? (char)('0' + (value - '0' + state.Offset) % 10)
                    : value;
            }
        });
}
