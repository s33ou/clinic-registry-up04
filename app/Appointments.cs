using System.Data;
using MySql.Data.MySqlClient;

namespace Clinic06;

internal sealed partial class Database
{
    private const string VisitFields = """
        a.id,a.slot_id,d.id AS doctor_id,{0} AS 'Талон',p.full_name AS 'Пациент',d.full_name AS 'Врач',
        s.starts_at AS 'Прием',d.room AS 'Кабинет',
        CASE WHEN a.status='cancelled' THEN 'Отменена'
             WHEN s.starts_at<=NOW() THEN 'Срок прошел' ELSE 'Активна' END AS 'Статус'
        """;
    private const string VisitJoin = """
        FROM appointments a JOIN patients p ON p.id=a.patient_id
        JOIN slots s ON s.id=a.slot_id JOIN doctors d ON d.id=s.doctor_id
        """;

    public DataTable Appointments(Session session, string search, DateTime? day = null)
    {
        if (session.Role == Security.Doctor)
        {
            if (session.DoctorId is not uint doctor || day is not DateTime date)
                throw new InvalidOperationException("Укажите дату приемов.");
            return Query("SELECT " + string.Format(VisitFields, TicketNumber) + " " + VisitJoin + """
                 WHERE d.id=@doctor AND a.status='active'
                 AND s.starts_at>=@day AND s.starts_at<DATE_ADD(@day,INTERVAL 1 DAY)
                 ORDER BY s.starts_at
                """, ("@doctor",doctor), ("@day",date.Date));
        }
        session.Require(Security.Registrar);
        return Query("SELECT " + string.Format(VisitFields,TicketNumber) + " " + VisitJoin + $"""
             WHERE LOCATE(@q,CONCAT_WS(' ',{TicketNumber},p.full_name,d.full_name,DATE_FORMAT(s.starts_at,'%d.%m.%Y')))>0
             ORDER BY a.id DESC
            """, ("@q",search));
    }

    public DataRow Ticket(Session session, uint id)
    {
        session.Require(Security.Registrar);
        var result = Query("SELECT " + string.Format(VisitFields,TicketNumber) + " " + VisitJoin + " WHERE a.id=@id",
            ("@id",id));
        if (result.Rows.Count != 1)
            throw new InvalidOperationException("Запись не найдена.");
        return result.Rows[0];
    }

    public DataTable History(Session session, string search)
    {
        session.Require(Security.Registrar);
        return Query($"""
            SELECT h.id,{TicketNumber} AS 'Талон',u.full_name AS 'Сотрудник',
                   CASE h.action WHEN 'booked' THEN 'Оформление'
                     WHEN 'moved' THEN 'Перенос' ELSE 'Отмена' END AS 'Действие',
                   old_s.starts_at AS 'Было',new_s.starts_at AS 'Стало',h.changed_at AS 'Изменено'
            FROM appointment_history h JOIN appointments a ON a.id=h.appointment_id
            JOIN users u ON u.id=h.employee_id
            LEFT JOIN slots old_s ON old_s.id=h.old_slot_id
            LEFT JOIN slots new_s ON new_s.id=h.new_slot_id
            WHERE LOCATE(@q,CONCAT_WS(' ',{TicketNumber},u.full_name))>0 ORDER BY h.id DESC
            """, ("@q",search));
    }

    public List<Choice> AvailableSlots(Session session, uint doctor, DateTime day)
    {
        session.Require(Security.Registrar);
        return Choices(Query("""
            SELECT s.id, DATE_FORMAT(s.starts_at,'%d.%m.%Y %H:%i')
            FROM slots s LEFT JOIN appointments a ON a.active_slot_id=s.id
            WHERE s.doctor_id=@doctor AND s.starts_at>=@day
              AND s.starts_at<DATE_ADD(@day,INTERVAL 1 DAY)
              AND s.starts_at>NOW() AND a.id IS NULL ORDER BY s.starts_at
            """, ("@doctor",doctor), ("@day",day.Date)));
    }

    public uint Book(Session session, uint patient, uint slot)
    {
        session.Require(Security.Registrar);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        RequireFutureSlot(connection, transaction, slot);
        using var insert = Command(connection, transaction, """
            INSERT INTO appointments(patient_id,slot_id) VALUES(@patient,@slot)
            """, ("@patient",patient), ("@slot",slot));
        insert.ExecuteNonQuery();
        uint appointment = checked((uint)insert.LastInsertedId);
        RecordChange(connection, transaction, session.Id, appointment, "booked", null, slot);
        transaction.Commit();
        return appointment;
    }

    public void ChangeAppointment(Session session, uint id, uint expectedSlot, uint? newSlot)
    {
        session.Require(Security.Registrar);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var find = Command(connection, transaction, """
            SELECT a.slot_id FROM appointments a JOIN slots s ON s.id=a.slot_id
            WHERE a.id=@id AND a.status='active' AND s.starts_at>NOW() FOR UPDATE
            """, ("@id",id));
        if (find.ExecuteScalar() is not object old || Id(old) != expectedSlot)
            throw new InvalidOperationException("Запись уже изменилась или срок приема прошел. Обновите список.");

        if (newSlot.HasValue)
        {
            if (newSlot == expectedSlot)
                throw new InvalidOperationException("Выберите другое время.");
            RequireFutureSlot(connection, transaction, newSlot.Value);
        }
        using var update = Command(connection, transaction,
            newSlot.HasValue ? "UPDATE appointments SET slot_id=@slot WHERE id=@id" :
                "UPDATE appointments SET status='cancelled' WHERE id=@id",
            ("@id",id), ("@slot",newSlot));
        update.ExecuteNonQuery();
        RecordChange(connection, transaction, session.Id, id, newSlot.HasValue ? "moved" : "cancelled",
            expectedSlot, newSlot);
        transaction.Commit();
    }

    private static void RequireFutureSlot(MySqlConnection connection, MySqlTransaction transaction, uint slot)
    {
        using var command = Command(connection, transaction,
            "SELECT id FROM slots WHERE id=@id AND starts_at>NOW()", ("@id",slot));
        if (command.ExecuteScalar() is null)
            throw new InvalidOperationException("Интервал удален или срок приема прошел. Выберите другое время.");
    }

    private static void RecordChange(MySqlConnection connection, MySqlTransaction transaction, uint employee,
        uint appointment, string action, uint? oldSlot, uint? newSlot)
    {
        using var command = Command(connection, transaction, """
            INSERT INTO appointment_history(appointment_id,employee_id,action,old_slot_id,new_slot_id)
            VALUES(@appointment,@employee,@action,@old,@new)
            """, ("@appointment",appointment), ("@employee",employee), ("@action",action),
            ("@old",oldSlot), ("@new",newSlot));
        command.ExecuteNonQuery();
    }
}
