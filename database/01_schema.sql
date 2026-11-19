CREATE DATABASE IF NOT EXISTS clinic06_up04
    CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
USE clinic06_up04;

CREATE TABLE IF NOT EXISTS roles (
    id TINYINT UNSIGNED PRIMARY KEY,
    code VARCHAR(20) NOT NULL UNIQUE,
    title VARCHAR(40) NOT NULL
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS specialties (
    id SMALLINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    title VARCHAR(70) NOT NULL UNIQUE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS doctors (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    full_name VARCHAR(100) NOT NULL,
    specialty_id SMALLINT UNSIGNED NOT NULL,
    room VARCHAR(10) NOT NULL,
    CONSTRAINT fk_doctors_specialty FOREIGN KEY (specialty_id)
        REFERENCES specialties(id) ON DELETE RESTRICT
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS patients (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    full_name VARCHAR(100) NOT NULL,
    birth_date DATE NOT NULL,
    policy CHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    phone_cipher VARCHAR(24) NOT NULL,
    CONSTRAINT uq_patients_policy UNIQUE (policy),
    CONSTRAINT ck_patients_policy CHECK (REGEXP_LIKE(policy, '^[0-9]{16}$', 'c'))
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS users (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    login VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL UNIQUE,
    full_name VARCHAR(100) NOT NULL,
    password_hash BINARY(32) NOT NULL,
    password_salt BINARY(16) NOT NULL,
    password_iterations INT UNSIGNED NOT NULL,
    role_id TINYINT UNSIGNED NULL,
    doctor_id INT UNSIGNED NULL UNIQUE,
    is_active BOOLEAN NOT NULL DEFAULT FALSE,
    registered_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_users_role FOREIGN KEY (role_id) REFERENCES roles(id) ON DELETE RESTRICT,
    CONSTRAINT fk_users_doctor FOREIGN KEY (doctor_id) REFERENCES doctors(id) ON DELETE RESTRICT,
    CONSTRAINT ck_users_activation CHECK (
        (is_active = FALSE AND role_id IS NULL AND doctor_id IS NULL) OR
        (is_active = TRUE AND role_id IS NOT NULL AND role_id IN (1, 2) AND doctor_id IS NULL) OR
        (is_active = TRUE AND role_id IS NOT NULL AND role_id = 3 AND doctor_id IS NOT NULL)
    ),
    CONSTRAINT ck_users_iterations CHECK (password_iterations BETWEEN 100000 AND 2000000)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS slots (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    doctor_id INT UNSIGNED NOT NULL,
    starts_at DATETIME NOT NULL,
    CONSTRAINT uq_slots_doctor_time UNIQUE (doctor_id, starts_at),
    CONSTRAINT fk_slots_doctor FOREIGN KEY (doctor_id)
        REFERENCES doctors(id) ON DELETE RESTRICT,
    CONSTRAINT ck_slots_boundary CHECK (MOD(MINUTE(starts_at), 20) = 0 AND SECOND(starts_at) = 0),
    INDEX ix_slots_starts_at (starts_at)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS appointments (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    patient_id INT UNSIGNED NOT NULL,
    slot_id INT UNSIGNED NOT NULL,
    status ENUM('active', 'cancelled') NOT NULL DEFAULT 'active',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    active_slot_id INT UNSIGNED GENERATED ALWAYS AS (
        CASE WHEN status = 'active' THEN slot_id ELSE NULL END
    ) STORED,
    CONSTRAINT uq_appointments_active_slot UNIQUE (active_slot_id),
    CONSTRAINT fk_appointments_patient FOREIGN KEY (patient_id)
        REFERENCES patients(id) ON DELETE RESTRICT,
    CONSTRAINT fk_appointments_slot FOREIGN KEY (slot_id)
        REFERENCES slots(id) ON DELETE RESTRICT
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS appointment_history (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    appointment_id INT UNSIGNED NOT NULL,
    employee_id INT UNSIGNED NOT NULL,
    action ENUM('booked', 'moved', 'cancelled') NOT NULL,
    old_slot_id INT UNSIGNED NULL,
    new_slot_id INT UNSIGNED NULL,
    changed_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_history_appointment FOREIGN KEY (appointment_id) REFERENCES appointments(id) ON DELETE RESTRICT,
    CONSTRAINT fk_history_employee FOREIGN KEY (employee_id) REFERENCES users(id) ON DELETE RESTRICT,
    CONSTRAINT fk_history_old_slot FOREIGN KEY (old_slot_id) REFERENCES slots(id) ON DELETE RESTRICT,
    CONSTRAINT fk_history_new_slot FOREIGN KEY (new_slot_id) REFERENCES slots(id) ON DELETE RESTRICT,
    CONSTRAINT ck_history_action CHECK (
        (action = 'booked' AND old_slot_id IS NULL AND new_slot_id IS NOT NULL) OR
        (action = 'moved' AND old_slot_id IS NOT NULL AND new_slot_id IS NOT NULL AND old_slot_id <> new_slot_id) OR
        (action = 'cancelled' AND old_slot_id IS NOT NULL AND new_slot_id IS NULL)
    )
) ENGINE=InnoDB;
