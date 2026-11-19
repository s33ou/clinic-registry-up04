using System.Data;
using CareerOffice;

namespace Clinic06;

internal sealed class RegistryPage : TabPage
{
    private readonly FlowLayoutPanel toolbar = new()
    {
        Dock=DockStyle.Top, Height=57, Padding=new Padding(9,10,8,6), WrapContents=false
    };
    private readonly Func<string,DataTable> load;
    public readonly TextBox Search = Ui.Text("",120);
    public readonly DataGridView Grid = new()
    {
        Dock=DockStyle.Fill, ReadOnly=true, AllowUserToAddRows=false, AllowUserToDeleteRows=false,
        MultiSelect=false, SelectionMode=DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible=false,
        BackgroundColor=Color.White, BorderStyle=BorderStyle.None, RowTemplate={ Height=30 }
    };

    public RegistryPage(string title, Func<string,DataTable> source, DateTimePicker? day = null) : base(title)
    {
        load=source;
        BackColor=Color.White;
        Padding=new Padding(8);
        var label=new Label { Text=day is null ? "Поиск" : "Дата",AutoSize=true,Margin=new Padding(0,7,7,0) };
        toolbar.Controls.Add(label);
        if (day is null)
        {
            Search.Width=180;
            Search.Margin=new Padding(0,3,9,0);
            Search.AccessibleName="Поиск";
            Search.KeyDown+=(_,e)=>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress=true;
                    Ui.Try(this,RefreshRows);
                }
            };
            toolbar.Controls.Add(Search);
            Add("Найти",RefreshRows,true);
            Add("Все записи",() => { Search.Clear(); RefreshRows(); });
        }
        else
        {
            day.Width=150;
            day.Margin=new Padding(0,3,9,0);
            day.ValueChanged+=(_,_)=>Ui.Try(this,RefreshRows);
            toolbar.Controls.Add(day);
            Add("Показать",RefreshRows,true);
        }
        Grid.EnableHeadersVisualStyles=false;
        Grid.ColumnHeadersDefaultCellStyle.BackColor=Ui.Navy;
        Grid.ColumnHeadersDefaultCellStyle.ForeColor=Color.White;
        Grid.ColumnHeadersHeight=36;
        Grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        Grid.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(241,248,247);
        Grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(205,233,228);
        Grid.DefaultCellStyle.SelectionForeColor=Ui.Navy;
        Grid.GridColor=Color.FromArgb(223,231,232);
        Controls.Add(Grid);
        Controls.Add(toolbar);
    }

    public void Add(string text, Action click, bool primary = false) =>
        toolbar.Controls.Add(Ui.Button(text,()=>Ui.Try(this,click),primary));

    public void RefreshRows()
    {
        Grid.DataSource=load(Search.Text.Trim());
        foreach (DataGridViewColumn column in Grid.Columns)
        {
            if (column.Name is "id" or "slot_id" or "doctor_id" or "specialty_id")
                column.Visible=false;
            if (column.ValueType==typeof(DateTime))
                column.DefaultCellStyle.Format=column.Name=="Дата рождения" ? "dd.MM.yyyy" : "dd.MM.yyyy HH:mm";
            if (column.Name is "Код" or "Кабинет")
                column.FillWeight=50;
            if (column.Name is "Врач" or "Пациент")
                column.FillWeight=125;
        }
    }

    public DataRow Selected => (Grid.CurrentRow?.DataBoundItem as DataRowView)?.Row ??
        throw new InvalidOperationException("Выберите строку в таблице.");
    public static uint RowId(DataRow row, string field="id") => Convert.ToUInt32(row[field]);
}

internal sealed class MainForm : Form
{
    private readonly Database database;
    private readonly Session session;
    internal readonly TabControl Tabs = new() { Dock=DockStyle.Fill, Padding=new Point(16,8) };

    public MainForm(Database database, Session session, Action logout)
    {
        this.database=database;
        this.session=session;
        Ui.Apply(this,"Регистратура поликлиники — "+RoleName(session.Role),new Size(1090,650));
        MinimumSize=new Size(990,560);
        var header=new Panel { Dock=DockStyle.Top, Height=91, Padding=new Padding(16,0,12,0), BackColor=Ui.Canvas };
        var title=new Label { Text="Регистратура поликлиники", Location=new Point(22,12), AutoSize=true,
            Font=new Font("Segoe UI",20,FontStyle.Bold) };
        var subtitle=new Label { Text=RoleName(session.Role)+"  ·  "+session.FullName+"  ·  MySQL "+database.Endpoint,
            Location=new Point(25,55),AutoSize=true,ForeColor=Color.DimGray };
        var tools=new FlowLayoutPanel { Dock=DockStyle.Right, Width=174, Padding=new Padding(8,23,0,0),WrapContents=false };
        tools.Controls.Add(Ui.Button("?",ShowHelp));
        tools.Controls.Add(Ui.Button("Выйти",logout));
        header.Controls.AddRange([title,subtitle,tools]);
        Controls.Add(Tabs);
        Controls.Add(header);
        switch (session.Role)
        {
            case Security.Registrar:
                AddPatients();
                AddAppointments();
                AddSlots(false);
                Tabs.TabPages.Add(new RegistryPage("История",q=>database.History(session,q)));
                break;
            case Security.Admin:
                AddUsers();
                AddDoctors();
                AddSlots(true);
                break;
            case Security.Doctor:
                var date=Ui.Date(DateTime.Today.AddDays(1));
                Tabs.TabPages.Add(new RegistryPage("Мои приемы",_=>database.Appointments(session,"",date.Value),date));
                break;
            default:
                throw new InvalidOperationException("Неизвестная роль сотрудника.");
        }
        Shown+=(_,_)=>RefreshSelected();
        Tabs.SelectedIndexChanged+=(_,_)=>RefreshSelected();
        KeyPreview=true;
        KeyDown+=(_,e)=>
        {
            if (e.KeyCode!=Keys.F1) return;
            e.Handled=true;
            ShowHelp();
        };
    }

    private static string RoleName(string role) => role switch
    {
        Security.Admin=>"Администратор",
        Security.Registrar=>"Регистратор",
        _=>"Врач"
    };
    private void RefreshSelected()
    {
        if (Tabs.SelectedTab is RegistryPage page)
            Ui.Try(this,page.RefreshRows);
    }

    private void AddPatients()
    {
        var page=new RegistryPage("Пациенты",q=>database.Patients(session,q));
        page.Add("Добавить",()=>PatientEditor(page,null));
        page.Add("Изменить",()=>PatientEditor(page,page.Selected));
        page.Add("Удалить",()=>Delete(page,"Удалить выбранную карточку пациента?",id=>database.DeletePatient(session,id)));
        Tabs.TabPages.Add(page);
    }

    private void PatientEditor(RegistryPage page,DataRow? row)
    {
        var name=Ui.Text(row?["Пациент"] as string ?? "");
        var birth=Ui.Date(row?["Дата рождения"] is DateTime value ? value : new DateTime(2000,1,1));
        var policy=Ui.Text(row?["Полис"] as string ?? "",24);
        var phone=Ui.Text(row?["Телефон"] as string ?? "",24);
        using var form=new EditForm(row is null ? "Новая карточка пациента" : "Изменение карточки пациента","Сохранить",
            ()=>database.SavePatient(session,row is null ? null : RegistryPage.RowId(row),name.Text,birth.Value,policy.Text,phone.Text),
            ("ФИО пациента",name),("Дата рождения",birth),("Номер полиса",policy),("Телефон",phone));
        OpenEditor(page,form);
    }

    private void AddAppointments()
    {
        var page=new RegistryPage("Записи на прием",q=>database.Appointments(session,q));
        page.Add("Записать",()=>AppointmentEditor(page,null));
        page.Add("Перенести",()=>AppointmentEditor(page,page.Selected));
        page.Add("Отменить",()=>
        {
            DataRow record=page.Selected;
            RequireActive(record);
            if (MessageBox.Show(this,"Подтвердить отмену талона "+record["Талон"]+"?","Отмена записи",
                MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes) return;
            database.ChangeAppointment(session,RegistryPage.RowId(record),RegistryPage.RowId(record,"slot_id"),null);
            page.RefreshRows();
        });
        page.Add("Word / PDF",()=>Export(page.Selected));
        Tabs.TabPages.Add(page);
    }

    private static void RequireActive(DataRow row)
    {
        if (!Equals(row["Статус"],"Активна"))
            throw new InvalidOperationException("Перенос и отмена доступны для активной записи.");
    }

    private void AppointmentEditor(RegistryPage page,DataRow? row)
    {
        if (row is not null) RequireActive(row);
        Control patient=row is null ? Ui.Choose(database.PatientChoices(session)) :
            new TextBox { Text=Convert.ToString(row["Пациент"]),ReadOnly=true };
        var doctor=Ui.Choose(database.DoctorChoices(session),row is null ? null : RegistryPage.RowId(row,"doctor_id"));
        var date=Ui.Date(row is null ? DateTime.Today.AddDays(1) : Convert.ToDateTime(row["Прием"]).Date);
        var slot=Ui.Choose([]);
        var status=new Label { AutoSize=true };
        void LoadTimes()
        {
            Ui.SetChoices(slot,doctor.SelectedItem is Choice choice ? database.AvailableSlots(session,choice.Id,date.Value) : []);
            status.Text=slot.Items.Count>0 ? "Выберите свободный интервал" : "Свободных интервалов на эту дату нет";
        }
        doctor.SelectedIndexChanged+=(_,_)=>Ui.Try(this,LoadTimes);
        date.ValueChanged+=(_,_)=>Ui.Try(this,LoadTimes);
        LoadTimes();
        uint? result=null;
        using var form=new EditForm(row is null ? "Запись пациента на прием" : "Перенос записи "+row["Талон"],
            row is null ? "Записать":"Подтвердить перенос",()=>
            {
                uint time=Ui.GetChoice(slot);
                if (row is null)
                    result=database.Book(session,Ui.GetChoice((ComboBox)patient),time);
                else
                    database.ChangeAppointment(session,RegistryPage.RowId(row),RegistryPage.RowId(row,"slot_id"),time);
            }, ("Пациент",patient),("Врач",doctor),("Дата",date),("Время",slot),("Доступность",status));
        if (form.ShowDialog(this)!=DialogResult.OK) return;
        if (result.HasValue)
            page.Search.Text=TicketDocuments.Number(result.Value);
        page.RefreshRows();
    }

    private void AddSlots(bool admin)
    {
        var page=new RegistryPage("Расписание",q=>database.Slots(session,q));
        if (admin)
        {
            page.Add("Добавить интервал",()=>
            {
                var doctor=Ui.Choose(database.DoctorChoices(session));
                var date=Ui.Date(DateTime.Today.AddDays(1).AddHours(10),true);
                using var form=new EditForm("Новый интервал","Сохранить",
                    ()=>database.AddSlot(session,Ui.GetChoice(doctor),date.Value),
                    ("Врач",doctor),("Дата и время",date));
                OpenEditor(page,form);
            });
            page.Add("Удалить интервал",()=>Delete(page,"Удалить интервал расписания?",id=>database.DeleteSlot(session,id)));
        }
        Tabs.TabPages.Add(page);
    }

    private void AddDoctors()
    {
        var page=new RegistryPage("Врачи",q=>database.Doctors(session,q));
        page.Add("Добавить",()=>DoctorEditor(page,null));
        page.Add("Изменить",()=>DoctorEditor(page,page.Selected));
        page.Add("Удалить",()=>Delete(page,"Удалить карточку врача?",id=>database.DeleteDoctor(session,id)));
        Tabs.TabPages.Add(page);
    }

    private void DoctorEditor(RegistryPage page,DataRow? row)
    {
        var name=Ui.Text(row?["Врач"] as string ?? "");
        var specialty=Ui.Choose(database.Specialties(session),row is null ? null : RegistryPage.RowId(row,"specialty_id"));
        var room=Ui.Text(row?["Кабинет"] as string ?? "",10);
        using var form=new EditForm("Карточка врача","Сохранить",
            ()=>database.SaveDoctor(session,row is null ? null : RegistryPage.RowId(row),name.Text,Ui.GetChoice(specialty),room.Text),
            ("ФИО врача",name),("Специальность",specialty),("Кабинет",room));
        OpenEditor(page,form);
    }

    private void AddUsers()
    {
        var page=new RegistryPage("Сотрудники",q=>database.Users(session,q));
        page.Add("Активировать",()=>
        {
            DataRow row=page.Selected;
            var role=Ui.Choose([new Choice(2,"Регистратор"),new Choice(3,"Врач")]);
            var doctor=Ui.Choose(database.DoctorChoices(session));
            doctor.Enabled=false;
            role.SelectedIndexChanged+=(_,_)=>doctor.Enabled=Ui.GetChoice(role)==3;
            using var form=new EditForm("Активация "+row["Логин"],"Активировать",()=>
            {
                uint selected=Ui.GetChoice(role);
                database.Activate(session,RegistryPage.RowId(row),selected,selected==3 ? Ui.GetChoice(doctor) : null);
            }, ("Роль сотрудника",role),("Карточка врача",doctor));
            OpenEditor(page,form);
        });
        Tabs.TabPages.Add(page);
    }

    private void Delete(RegistryPage page,string text,Action<uint> delete)
    {
        uint id=RegistryPage.RowId(page.Selected);
        if (MessageBox.Show(this,text,"Подтверждение",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes) return;
        delete(id);
        page.RefreshRows();
    }
    private void OpenEditor(RegistryPage page,EditForm form)
    {
        if (form.ShowDialog(this)==DialogResult.OK) page.RefreshRows();
    }

    private void Export(DataRow row)
    {
        using var chooser=new SaveFileDialog { Title="Сохранить талон", FileName="Талон "+row["Талон"],
            Filter="Документ Word (*.docx)|*.docx|Документ PDF (*.pdf)|*.pdf", AddExtension=true,OverwritePrompt=true };
        if (chooser.ShowDialog(this)==DialogResult.OK)
            TicketDocuments.Export(database,session,RegistryPage.RowId(row),chooser.FileName);
    }

    private void ShowHelp()
    {
        string[] lines=session.Role switch
        {
            Security.Registrar=>[
                "В разделе «Пациенты» найдите карточку по ФИО или номеру полиса. При первом обращении добавьте карточку.",
                "В разделе «Записи на прием» нажмите «Записать», выберите пациента, врача, дату и свободное время.",
                "Для переноса и отмены сначала найдите активную запись по номеру талона. Изменения видны на вкладке «История».",
                "Кнопка «Word / PDF» сохраняет выбранный талон. Для выгрузки используется установленный Microsoft Word."],
            Security.Admin=>[
                "В разделе «Сотрудники» выберите заявку, нажмите «Активировать» и назначьте роль. Для врача выберите его карточку.",
                "В разделе «Врачи» доступны справочник и кабинеты. На вкладке «Расписание» добавляйте интервалы по 20 минут."],
            _=>["На вкладке «Мои приемы» укажите дату. После нажатия «Показать» выводятся записи, относящиеся к вашему расписанию."]
        };
        HelpWindow.Show(this,"Работа с регистратурой",lines);
    }
}
