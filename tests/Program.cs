using System.Text;
using Clinic06;

Console.OutputEncoding = Encoding.UTF8;
const string phone = "+7 901 028-39-41";
const string encryptedPhone = "+0 234 351-62-74";
const string password = "Тестовый пароль 2026";
var digest = Security.CreateDigest(password);
var secondDigest = Security.CreateDigest(password);
var registrar = new Session(2, "Учебный регистратор", Security.Registrar, null);
var results = new (string Name, Func<bool> Check)[]
{
    ("Телефон шифруется с сохранением формата", () => DemoPhoneCipher.Encrypt(phone) == encryptedPhone),
    ("Телефон восстанавливается после чтения", () => DemoPhoneCipher.Decrypt(encryptedPhone) == phone),
    ("Все десять цифр обратимы", () => DemoPhoneCipher.Decrypt(DemoPhoneCipher.Encrypt("0123456789")) == "0123456789"),
    ("Правильный пароль подтверждается", () => Security.Verify(password, digest.Hash, digest.Salt, Security.Iterations)),
    ("Другой пароль отклоняется", () => !Security.Verify("Иной пароль", digest.Hash, digest.Salt, Security.Iterations)),
    ("Одинаковым паролям выдаются разные соли", () => !digest.Salt.SequenceEqual(secondDigest.Salt)),
    ("Логин приводится к нижнему регистру", () => Security.NormalizeLogin("  Registrar_06  ") == "registrar_06"),
    ("Логин проверяется по формату", () => Security.ValidLogin("registrar_06") && !Security.ValidLogin("reg")),
    ("Регистратор может выполнить свою операцию", () => { registrar.Require(Security.Registrar); return true; }),
    ("Операция администратора требует другую роль", () => RejectsDifferentRole(registrar))
};

int passed = 0;
foreach (var (name, check) in results)
{
    bool ok = check();
    Console.WriteLine($"{(ok ? "ПРОЙДЕНО" : "ОШИБКА")}  {name}");
    if (ok) passed++;
}
Console.WriteLine($"Итог {passed} из {results.Length}");
return passed == results.Length ? 0 : 1;

static bool RejectsDifferentRole(Session registrar)
{
    try { registrar.Require(Security.Admin); }
    catch (InvalidOperationException) { return true; }
    return false;
}
