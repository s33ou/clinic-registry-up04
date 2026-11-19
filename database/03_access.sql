CREATE ROLE IF NOT EXISTS 'clinic5_admin', 'clinic5_registrar', 'clinic5_doctor';

GRANT SELECT ON clinic06_up04.roles TO 'clinic5_admin';
GRANT SELECT ON clinic06_up04.specialties TO 'clinic5_admin';
GRANT SELECT (id, login, full_name, role_id, doctor_id, is_active, registered_at),
      UPDATE (role_id, doctor_id, is_active)
ON clinic06_up04.users TO 'clinic5_admin';
GRANT SELECT, INSERT, UPDATE, DELETE ON clinic06_up04.doctors TO 'clinic5_admin';
GRANT SELECT, INSERT, DELETE ON clinic06_up04.slots TO 'clinic5_admin';

GRANT SELECT, INSERT, UPDATE, DELETE ON clinic06_up04.patients TO 'clinic5_registrar';
GRANT SELECT ON clinic06_up04.specialties TO 'clinic5_registrar';
GRANT SELECT ON clinic06_up04.doctors TO 'clinic5_registrar';
GRANT SELECT ON clinic06_up04.slots TO 'clinic5_registrar';
GRANT SELECT, INSERT, UPDATE (slot_id, status) ON clinic06_up04.appointments TO 'clinic5_registrar';
GRANT SELECT, INSERT ON clinic06_up04.appointment_history TO 'clinic5_registrar';

-- имя отдельного SQL-пользователя однозначно связано с идентификатором врача
CREATE OR REPLACE SQL SECURITY DEFINER VIEW clinic06_up04.v_clinic5_my_visits AS
SELECT a.id AS appointment_id, p.full_name AS patient_name, s.starts_at, d.room
FROM clinic06_up04.appointments AS a
JOIN clinic06_up04.patients AS p ON p.id = a.patient_id
JOIN clinic06_up04.slots AS s ON s.id = a.slot_id
JOIN clinic06_up04.doctors AS d ON d.id = s.doctor_id
WHERE a.status = 'active'
  AND CONCAT('clinic5_doc_', d.id) = SUBSTRING_INDEX(USER(), '@', 1);

GRANT SELECT ON clinic06_up04.v_clinic5_my_visits TO 'clinic5_doctor';
