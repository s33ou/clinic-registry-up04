using System.Data;
using CareerOffice;

namespace Clinic06;

internal static class TicketDocuments
{
    public static string Number(uint id) => $"TAL-{id:D6}";

    public static void Export(Database database, Session session, uint id, string path)
    {
        DataRow row = database.Ticket(session,id);
        using var document = new RichTextBox { DetectUrls=false };
        using var title = new Font("Segoe UI",18,FontStyle.Bold);
        using var text = new Font("Segoe UI",11);
        document.SelectionColor=Ui.Navy;
        document.SelectionFont=title;
        document.SelectedText=$"Регистратура поликлиники\r\nТалон {Number(id)}\r\n\r\n";
        document.SelectionFont=text;
        document.SelectionColor=Color.Black;
        document.SelectedText=$"Пациент: {row["Пациент"]}\r\nВрач: {row["Врач"]}\r\n" +
            $"Дата и время: {Convert.ToDateTime(row["Прием"]):dd.MM.yyyy HH:mm}\r\n" +
            $"Кабинет: {row["Кабинет"]}\r\nСтатус: {row["Статус"]}\r\n";
        WordDocuments.Export(document,path);
    }
}
