using System.Runtime.InteropServices;

namespace CareerOffice;

internal static class WordDocuments
{
    private const int RtfFormat = 6;
    private const int DocxFormat = 16;
    private const int PdfFormat = 17;
    private const int DoNotSave = 0;
    private const int ForceDisableMacros = 3;

    public static void LoadDocx(string path, RichTextBox editor)
    {
        string intermediate = TemporaryPath(Path.GetTempPath(), ".rtf");
        try
        {
            ConvertWithWord(Path.GetFullPath(path), intermediate, RtfFormat);
            editor.LoadFile(intermediate, RichTextBoxStreamType.RichText);
        }
        finally
        {
            DeleteTemporary(intermediate);
        }
    }

    public static void Export(RichTextBox editor, string path)
    {
        string destination = Path.GetFullPath(path);
        string extension = Path.GetExtension(destination).ToLowerInvariant();
        int format = extension switch
        {
            ".docx" => DocxFormat,
            ".pdf" => PdfFormat,
            _ => throw new ArgumentException("Для экспорта выберите DOCX или PDF.", nameof(path))
        };
        string source = TemporaryPath(Path.GetTempPath(), ".rtf");
        string intermediate = TemporaryPath(Path.GetDirectoryName(destination)!, extension);
        try
        {
            editor.SaveFile(source, RichTextBoxStreamType.RichText);
            ConvertWithWord(source, intermediate, format);
            File.Move(intermediate, destination, overwrite: true);
        }
        finally
        {
            DeleteTemporary(source);
            DeleteTemporary(intermediate);
        }
    }

    private static void ConvertWithWord(string source, string destination, int format)
    {
        Type wordType = Type.GetTypeFromProgID("Word.Application")
            ?? throw new InvalidOperationException("Для работы с DOCX и прямого экспорта PDF установите Microsoft Word.");
        object? application = null;
        object? options = null;
        object? documents = null;
        object? document = null;
        int? previousSecurity = null;
        int? previousAlerts = null;
        bool? previousLinkUpdates = null;
        bool ownsEmptySession = false;

        try
        {
            application = Activator.CreateInstance(wordType)
                ?? throw new InvalidOperationException("Не удалось запустить Microsoft Word.");
            dynamic word = application;
            documents = word.Documents;
            dynamic collection = documents!;
            ownsEmptySession = collection.Count == 0 && !word.Visible;
            previousSecurity = word.AutomationSecurity;
            previousAlerts = word.DisplayAlerts;
            word.AutomationSecurity = ForceDisableMacros;
            word.DisplayAlerts = DoNotSave;
            options = word.Options;
            dynamic wordOptions = options!;
            previousLinkUpdates = wordOptions.UpdateLinksAtOpen;
            wordOptions.UpdateLinksAtOpen = false;

            // Входной файл открывается только для чтения и вне списка недавних документов.
            document = collection.Open(FileName: source, ConfirmConversions: false, ReadOnly: true,
                AddToRecentFiles: false, Visible: false, NoEncodingDialog: true);
            dynamic current = document!;
            if (format == PdfFormat)
                current.ExportAsFixedFormat(OutputFileName: destination, ExportFormat: PdfFormat, OpenAfterExport: false,
                    IncludeDocProps: false);
            else
                current.SaveAs2(FileName: destination, FileFormat: format, AddToRecentFiles: false);
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException("Microsoft Word не смог обработать документ. Проверьте файл и запуск Word.", ex);
        }
        finally
        {
            if (document is not null)
                BestEffort(() => ((dynamic)document).Close(DoNotSave));
            Release(document);
            if (options is not null && previousLinkUpdates.HasValue)
                BestEffort(() => ((dynamic)options).UpdateLinksAtOpen = previousLinkUpdates.Value);
            Release(options);
            if (application is not null)
            {
                if (previousSecurity.HasValue)
                    BestEffort(() => ((dynamic)application).AutomationSecurity = previousSecurity.Value);
                if (previousAlerts.HasValue)
                    BestEffort(() => ((dynamic)application).DisplayAlerts = previousAlerts.Value);
                if (ownsEmptySession)
                    BestEffort(() => ((dynamic)application).Quit(DoNotSave));
            }
            Release(documents);
            Release(application);
        }
    }

    private static string TemporaryPath(string folder, string extension) =>
        Path.Combine(folder, $".career-{Guid.NewGuid():N}{extension}");

    private static void DeleteTemporary(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void Release(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
            Marshal.ReleaseComObject(instance);
    }

    private static void BestEffort(Action cleanup)
    {
        try { cleanup(); }
        catch (COMException) { }
    }
}
