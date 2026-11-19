using MySql.Data.MySqlClient;

namespace Clinic06;

internal static class Ui
{
    public static readonly Color Navy = Color.FromArgb(22, 49, 65);
    public static readonly Color Accent = Color.FromArgb(20, 108, 103);
    public static readonly Color Canvas = Color.FromArgb(242, 247, 248);

    public static Button Button(string text, Action? action = null, bool primary = false)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 34, Padding = new Padding(9,3,9,3),
            Margin = new Padding(0,0,7,0), UseVisualStyleBackColor = !primary };
        if (primary)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.ForeColor = Color.White;
            button.BackColor = Accent;
        }
        if (action is not null)
            button.Click += (_,_) => action();
        return button;
    }

    public static void Apply(Form form, string title, Size size)
    {
        form.Text = title;
        form.ClientSize = size;
        form.StartPosition = FormStartPosition.CenterScreen;
        form.Font = new Font("Segoe UI",10);
        form.ForeColor = Navy;
        form.BackColor = Canvas;
    }

    public static bool Try(IWin32Window owner, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception error) when (error is InvalidOperationException or MySqlException or IOException
            or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            string message = error is MySqlException mysql ? mysql.Number switch
            {
                1062 when mysql.Message.Contains("active_slot") => "Это время уже занято. Выберите другой интервал.",
                1062 when mysql.Message.Contains("policy") => "Карточка с этим полисом уже существует.",
                1062 => "Такая запись уже есть. Проверьте введенные данные.",
                1451 or 1452 => "Запись связана с другими данными. Проверьте выбранную карточку или время.",
                1205 or 1213 => "Данные одновременно меняет другой сотрудник. Обновите список и повторите.",
                _ => "Не удалось выполнить операцию MySQL. Проверьте подключение базы."
            } : error.Message;
            MessageBox.Show(owner,message,"Регистратура",MessageBoxButtons.OK,MessageBoxIcon.Information);
            return false;
        }
    }

    public static TextBox Text(string value = "", int max = 100) => new() { Text = value, MaxLength = max };
    public static DateTimePicker Date(DateTime value, bool time = false) => new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = time ? "dd.MM.yyyy HH:mm" : "dd.MM.yyyy",
        Value = value
    };

    public static ComboBox Choose(IEnumerable<Choice> choices, uint? selected = null)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        SetChoices(combo,choices,selected);
        return combo;
    }

    public static void SetChoices(ComboBox combo, IEnumerable<Choice> choices, uint? selected = null)
    {
        Choice[] items = choices.ToArray();
        combo.Items.Clear();
        combo.Items.AddRange(items);
        int index = selected.HasValue ? Array.FindIndex(items,item=>item.Id==selected) : -1;
        combo.SelectedIndex = index >= 0 ? index : items.Length == 0 ? -1 : 0;
    }

    public static uint GetChoice(ComboBox combo) => combo.SelectedItem is Choice choice ? choice.Id :
        throw new InvalidOperationException("Выберите значение из списка.");
}

internal sealed class EditForm : Form
{
    private readonly TableLayoutPanel layout = new()
    {
        Dock=DockStyle.Top, AutoSize=true, ColumnCount=2, Padding=new Padding(20,20,20,8)
    };

    public EditForm(string title, string button, Action save, params (string Label, Control Input)[] fields)
    {
        Ui.Apply(this,title,new Size(585, 84 + 46 * fields.Length));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,177));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for (int i=0;i<fields.Length;i++)
        {
            var (label,input)=fields[i];
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
            layout.Controls.Add(new Label { Text=label, AutoSize=true, Anchor=AnchorStyles.Left },0,i);
            input.Anchor=AnchorStyles.Left|AnchorStyles.Right;
            input.AccessibleName=label;
            layout.Controls.Add(input,1,i);
        }
        var buttons = new FlowLayoutPanel { Dock=DockStyle.Bottom, Height=57, FlowDirection=FlowDirection.RightToLeft,
            Padding=new Padding(12,7,16,5) };
        var cancel = Ui.Button("Закрыть");
        cancel.DialogResult = DialogResult.Cancel;
        var ok = Ui.Button(button, () =>
        {
            if (Ui.Try(this,save))
                DialogResult = DialogResult.OK;
        },true);
        buttons.Controls.AddRange([cancel,ok]);
        Controls.AddRange([layout,buttons]);
        AcceptButton=ok;
        CancelButton=cancel;
    }
}

internal sealed class LoginForm : Form
{
    public event Action<Session>? SignedIn;

    public LoginForm(Database database)
    {
        Ui.Apply(this,"Вход — регистратура поликлиники",new Size(560,385));
        FormBorderStyle=FormBorderStyle.FixedSingle;
        MaximizeBox=false;
        var header=new Label { Text="Регистратура поликлиники",Location=new Point(32,20), Size=new Size(496,42),
            Font=new Font("Segoe UI",20,FontStyle.Bold) };
        var note=new Label { Text="Вход сотрудников", Location=new Point(34,72),Size=new Size(490,28) };
        var login=Ui.Text("",32);
        var pass=Ui.Text("",120);
        pass.UseSystemPasswordChar = true;
        AddField("Логин",login,117);
        AddField("Пароль",pass,187);
        var enter=Ui.Button("Войти", () => Ui.Try(this,() => SignedIn?.Invoke(database.SignIn(login.Text,pass.Text))),true);
        var register=Ui.Button("Подать заявку на регистрацию", () => OpenRegistration(database));
        enter.Location=new Point(34,269);
        register.Location=new Point(147,269);
        var footer=new Label { Text="Учебная система  ·  MySQL " + database.Endpoint, Location=new Point(34,339),
            Size=new Size(490,28), ForeColor=Color.DimGray };
        Controls.AddRange([header,note,enter,register,footer]);
        AcceptButton=enter;

        void AddField(string caption, Control control, int top)
        {
            Controls.Add(new Label { Text=caption, Location=new Point(34,top),Size=new Size(490,23) });
            control.AccessibleName=caption;
            control.SetBounds(34,top+25,490,31);
            Controls.Add(control);
        }
    }

    internal void OpenRegistration(Database database)
    {
        using var form = Registration(database);
        if (form.ShowDialog(this)==DialogResult.OK)
            MessageBox.Show(this,"Заявка создана. Администратор назначит роль и активирует учетную запись.",
                "Регистрация",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }

    internal static EditForm Registration(Database database)
    {
        var name=Ui.Text();
        var login=Ui.Text("",32);
        var pass=Ui.Text("",120);
        var confirm=Ui.Text("",120);
        pass.UseSystemPasswordChar=confirm.UseSystemPasswordChar=true;
        return new EditForm("Заявка на регистрацию","Подать заявку",
            () => database.Register(name.Text,login.Text,pass.Text,confirm.Text),
            ("ФИО сотрудника",name),("Логин",login),("Пароль",pass),("Повтор пароля",confirm));
    }
}
