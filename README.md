# Регистратура поликлиники

Учебный вариант № 6 УП.04. Приложение C# Windows Forms хранит учебные карточки, расписание и талоны в MySQL.

## Запуск

1. Используйте Windows x64, PowerShell 7, .NET SDK с поддержкой .NET 8 и MySQL Community Server 8.0.
2. Из корня проекта выполните команды, указав свой путь к MySQL:

   ```powershell
   dotnet publish app/Clinic06.csproj -c Release -r win-x64 --self-contained true -o bin
   pwsh -NoProfile -File scripts/Service.ps1 -Action Start -MySqlHome 'C:\MySQL\mysql-8.0.46-winx64'
   ```

3. Для демонстрации используйте учетные записи `registrar`, `doctor` или `admin` с учебным паролем `ClinicDemo!06`. Для выгрузки талона нужен настольный Word.
4. Чтобы остановить учебную базу, закройте приложение и выполните `pwsh -NoProfile -File scripts/Service.ps1 -Action Stop`.

## Автоматические проверки

```powershell
dotnet run --project tests/Clinic06.Tests.csproj -c Release
```

На вкладке **Actions** доступны результаты сборки и десяти проверок логики. Конвейер запускается при добавлении коммита, обновлении запроса на слияние и вручную. Автотесты используют тот же код нормализации логина, проверки пароля, ролей и учебного шифрования телефона, что и приложение. Проверки формы, MySQL и экспорта в Word выполняются отдельно на локальном стенде.

Данные в `database` вымышленные. Сценарий запуска поднимает MySQL на локальном адресе. Учебное шифрование телефона предназначено для демонстрации.

## Резервное копирование

Сценарии `scripts/Backup.ps1` и `scripts/Restore.ps1` сохраняют и восстанавливают SQL-снимок. Просмотрите параметры через `Get-Help ./scripts/Backup.ps1 -Syntax`. Для восстановления создайте отдельный пустой экземпляр командой `./scripts/Service.ps1 -Action PrepareEmpty -State $restore -MySqlHome $mysql`. После импорта назначьте права командой `./scripts/Service.ps1 -Action InstallAccess -State $restore -MySqlHome $mysql`.
