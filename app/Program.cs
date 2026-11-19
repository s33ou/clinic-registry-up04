using System.Text.Json;
using MySql.Data.MySqlClient;

namespace Clinic06;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            string file = args.FirstOrDefault() ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Clinic06-UP04-Case6", "connection.json");
            var settings = JsonSerializer.Deserialize<ConnectionSettings>(File.ReadAllText(file),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("Файл подключения пуст.");
            var database = new Database(settings);
            database.CheckConnection();
            using var context = new ClinicContext(database);
            Application.Run(context);
        }
        catch (Exception error) when (error is IOException or JsonException or MySqlException or InvalidOperationException)
        {
            MessageBox.Show("Запустите приложение через «Запустить регистратуру.cmd».\r\n\r\n" + error.Message,
                "Регистратура", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal sealed class ClinicContext : ApplicationContext
{
    private readonly Database database;

    public ClinicContext(Database database)
    {
        this.database = database;
        Login();
    }

    public void Login()
    {
        var login = new LoginForm(database);
        login.SignedIn += session => Switch(new MainForm(database, session, Login));
        Switch(login);
    }

    private void Switch(Form next)
    {
        Form? previous = MainForm;
        MainForm = next;
        next.Show();
        previous?.Close();
    }
}
