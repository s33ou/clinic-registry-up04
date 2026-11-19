using System.Data;
using MySql.Data.MySqlClient;

namespace Clinic06;

internal sealed record ConnectionSettings(string Server, uint Port, string Database, string User, string Password);
internal sealed record Choice(uint Id, string Text)
{
    public override string ToString() => Text;
}

internal sealed partial class Database(ConnectionSettings settings)
{
    internal const string TicketNumber = "CONCAT('TAL-', LPAD(a.id, GREATEST(6,LENGTH(a.id)), '0'))";
    public string Endpoint => $"{settings.Server}:{settings.Port}";

    private MySqlConnection Open()
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = settings.Server,
            Port = settings.Port,
            Database = settings.Database,
            UserID = settings.User,
            Password = settings.Password,
            CharacterSet = "utf8mb4",
            SslMode = MySqlSslMode.Required,
            ConnectionTimeout = 5,
            DefaultCommandTimeout = 10
        };
        var connection = new MySqlConnection(builder.ConnectionString);
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static MySqlCommand Command(MySqlConnection connection, MySqlTransaction? transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private DataTable Query(string sql, params (string, object?)[] parameters)
    {
        using var connection = Open();
        using var command = Command(connection, null, sql, parameters);
        var table = new DataTable();
        using var adapter = new MySqlDataAdapter(command) { MissingSchemaAction = MissingSchemaAction.Add };
        adapter.Fill(table);
        return table;
    }

    private uint Save(string sql, params (string, object?)[] parameters)
    {
        using var connection = Open();
        using var command = Command(connection, null, sql, parameters);
        command.ExecuteNonQuery();
        return command.LastInsertedId > 0 ? checked((uint)command.LastInsertedId) : 0;
    }

    private static uint Id(object value) => Convert.ToUInt32(value);
    private static List<Choice> Choices(DataTable table) =>
        table.Rows.Cast<DataRow>().Select(row => new Choice(Id(row[0]), (string)row[1])).ToList();

    public void CheckConnection() => Query("SELECT id FROM roles LIMIT 1");

    public Session SignIn(string name, string password)
    {
        const string sql = """
            SELECT u.id,u.full_name,u.password_hash,u.password_salt,u.password_iterations,
                   u.is_active,r.code,u.doctor_id
            FROM users u LEFT JOIN roles r ON r.id=u.role_id WHERE u.login=@login
            """;
        var rows = Query(sql, ("@login", Security.NormalizeLogin(name)));
        if (rows.Rows.Count != 1)
            throw new InvalidOperationException("Неверный логин или пароль.");
        DataRow row = rows.Rows[0];
        if (!Security.Verify(password, (byte[])row[2], (byte[])row[3], Convert.ToInt32(row[4])))
            throw new InvalidOperationException("Неверный логин или пароль.");
        if (!Convert.ToBoolean(row[5]) || row[6] is not string role)
            throw new InvalidOperationException("Учетная запись ожидает активации администратором.");
        return new Session(Id(row[0]), (string)row[1], role, row.IsNull(7) ? null : Id(row[7]));
    }

    public uint Register(string fullName, string login, string password, string confirmation)
    {
        fullName = fullName.Trim();
        login = Security.NormalizeLogin(login);
        if (fullName.Length is < 2 or > 100 || !Security.ValidLogin(login))
            throw new InvalidOperationException("Введите ФИО и логин из 4–32 латинских букв, цифр или символов _ . -.");
        if (password.Length is < 10 or > 120 || password.All(char.IsWhiteSpace))
            throw new InvalidOperationException("Пароль должен содержать от 10 до 120 символов.");
        if (password != confirmation)
            throw new InvalidOperationException("Пароли не совпадают.");
        var (hash, salt) = Security.CreateDigest(password);
        const string sql = """
            INSERT INTO users(full_name,login,password_hash,password_salt,password_iterations)
            VALUES(@full_name,@login,@hash,@salt,@iterations)
            """;
        return Save(sql, ("@full_name",fullName), ("@login",login), ("@hash",hash), ("@salt",salt),
            ("@iterations",Security.Iterations));
    }

    public DataTable Users(Session session, string search)
    {
        session.Require(Security.Admin);
        return Query("""
            SELECT u.id,u.login AS 'Логин',u.full_name AS 'Сотрудник',COALESCE(r.title,'—') AS 'Роль',
                   IF(u.is_active,'Активна','Ожидает активации') AS 'Статус',u.registered_at AS 'Дата заявки'
            FROM users u LEFT JOIN roles r ON r.id=u.role_id
            WHERE LOCATE(@q,CONCAT_WS(' ',u.full_name,u.login))>0 ORDER BY u.id DESC
            """, ("@q",search));
    }

    public void Activate(Session session, uint user, uint role, uint? doctor)
    {
        session.Require(Security.Admin);
        if (role is not (2 or 3) || (role == 3) != doctor.HasValue)
            throw new InvalidOperationException("Выберите роль и укажите врача для соответствующей роли.");
        using var connection = Open();
        using var update = Command(connection, null, """
            UPDATE users SET role_id=@role, doctor_id=@doctor, is_active=TRUE
            WHERE id=@id AND is_active=FALSE
            """, ("@role",role), ("@doctor",doctor), ("@id",user));
        if (update.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Выберите учетную запись, ожидающую активации.");
    }

    public DataTable Patients(Session session, string search)
    {
        session.Require(Security.Registrar);
        var matches = Query("""
            SELECT id,id AS 'Код',full_name AS 'Пациент',birth_date AS 'Дата рождения',
                   policy AS 'Полис', phone_cipher AS 'Телефон'
            FROM patients
            WHERE LOCATE(@q, CONCAT_WS(' ',full_name,policy))>0
               OR LOCATE(@phone, phone_cipher)>0
            ORDER BY id
            """, ("@q", search), ("@phone", DemoPhoneCipher.Encrypt(search)));
        foreach (DataRow row in matches.Rows)
            row["Телефон"] = DemoPhoneCipher.Decrypt((string)row["Телефон"]);
        return matches;
    }

    public uint SavePatient(Session session, uint? id, string name, DateTime birth, string number, string phone)
    {
        session.Require(Security.Registrar);
        name = name.Trim();
        phone = phone.Trim();
        string policy = number.Replace(" ", "").Replace("-", "");
        if (policy.Length != 16 || policy.Any(c => c is < '0' or > '9'))
            throw new InvalidOperationException("Номер полиса должен содержать 16 цифр.");
        if (name.Length is < 2 or > 100 || birth.Date > DateTime.Today ||
            birth.Year < 1900 || phone.Length is < 5 or > 24 || phone.Count(char.IsAsciiDigit) < 5)
            throw new InvalidOperationException("Проверьте ФИО, дату рождения и контактный телефон.");
        const string insert = "INSERT INTO patients(full_name,birth_date,policy,phone_cipher) VALUES(@name,@birth,@policy,@phone)";
        const string update = "UPDATE patients SET full_name=@name,birth_date=@birth,policy=@policy,phone_cipher=@phone WHERE id=@id";
        uint saved = Save(id.HasValue ? update : insert, ("@id",id), ("@name",name), ("@birth",birth.Date),
            ("@policy",policy), ("@phone",DemoPhoneCipher.Encrypt(phone)));
        return id ?? saved;
    }

    public void DeletePatient(Session session, uint id)
    {
        session.Require(Security.Registrar);
        Save("DELETE FROM patients WHERE id=@id", ("@id",id));
    }

    public List<Choice> PatientChoices(Session session)
    {
        session.Require(Security.Registrar);
        return Choices(Query("SELECT id,CONCAT(full_name,' · ',policy) FROM patients ORDER BY full_name"));
    }

    public DataTable Doctors(Session session, string search)
    {
        session.Require(Security.Admin);
        return Query("""
            SELECT d.id,d.full_name AS 'Врач',s.title AS 'Специальность',d.room AS 'Кабинет',d.specialty_id
            FROM doctors d JOIN specialties s ON s.id=d.specialty_id
            WHERE LOCATE(@q,CONCAT_WS(' ',d.full_name,s.title,d.room))>0 ORDER BY d.id
            """, ("@q",search));
    }

    public List<Choice> Specialties(Session session)
    {
        session.Require(Security.Admin);
        return Choices(Query("SELECT id,title FROM specialties ORDER BY title"));
    }

    public List<Choice> DoctorChoices(Session session)
    {
        if (session.Role != Security.Admin)
            session.Require(Security.Registrar);
        return Choices(Query("""
            SELECT d.id,CONCAT(d.full_name,' · ',s.title) FROM doctors d
            JOIN specialties s ON s.id=d.specialty_id ORDER BY d.full_name
            """));
    }

    public void SaveDoctor(Session session, uint? id, string name, uint specialty, string room)
    {
        session.Require(Security.Admin);
        name = name.Trim();
        room = room.Trim();
        if (name.Length is < 2 or > 100 || room.Length is < 1 or > 10)
            throw new InvalidOperationException("Заполните ФИО и номер кабинета.");
        const string insert = "INSERT INTO doctors(full_name,specialty_id,room) VALUES(@name,@specialty,@room)";
        const string update = "UPDATE doctors SET full_name=@name,specialty_id=@specialty,room=@room WHERE id=@id";
        Save(id.HasValue ? update : insert, ("@id",id), ("@name",name), ("@specialty",specialty), ("@room",room));
    }

    public void DeleteDoctor(Session session, uint id)
    {
        session.Require(Security.Admin);
        Save("DELETE FROM doctors WHERE id=@id", ("@id",id));
    }

    public DataTable Slots(Session session, string search)
    {
        if (session.Role != Security.Admin)
            session.Require(Security.Registrar);
        return Query("""
            SELECT s.id,s.doctor_id,d.full_name AS 'Врач',d.room AS 'Кабинет',s.starts_at AS 'Начало приема',
                   CASE WHEN s.starts_at<=NOW() THEN 'Прошел'
                        WHEN a.id IS NULL THEN 'Свободен' ELSE 'Занят' END AS 'Статус'
            FROM slots s JOIN doctors d ON d.id=s.doctor_id
            LEFT JOIN appointments a ON a.active_slot_id=s.id
            WHERE LOCATE(@q,CONCAT_WS(' ',d.full_name,d.room,DATE_FORMAT(s.starts_at,'%d.%m.%Y'))) > 0
            ORDER BY s.starts_at,d.full_name
            """, ("@q",search));
    }

    public void AddSlot(Session session, uint doctor, DateTime starts)
    {
        session.Require(Security.Admin);
        if (starts <= DateTime.Now || starts.Minute % 20 != 0)
            throw new InvalidOperationException("Выберите будущее время с минутами 00, 20 или 40.");
        Save("INSERT INTO slots(doctor_id,starts_at) VALUES(@doctor,@starts)", ("@doctor",doctor), ("@starts",starts));
    }

    public void DeleteSlot(Session session, uint id)
    {
        session.Require(Security.Admin);
        Save("DELETE FROM slots WHERE id=@id", ("@id",id));
    }
}
