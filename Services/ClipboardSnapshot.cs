using System.Runtime.InteropServices;

namespace DoriDeck.Services;

internal sealed class ClipboardSnapshot
{
    private readonly DataObject? _data;

    private ClipboardSnapshot(DataObject? data)
    {
        _data = data;
    }

    public static ClipboardSnapshot Capture(int retryCount, TimeSpan retryDelay)
    {
        return RunInSta(() =>
        {
            var source = RetryClipboard(Clipboard.GetDataObject, retryCount, retryDelay);

            if (source is null)
            {
                return new ClipboardSnapshot(null);
            }

            var copy = new DataObject();

            foreach (var format in source.GetFormats(autoConvert: false))
            {
                try
                {
                    var value = source.GetData(format, autoConvert: false);

                    if (value is not null)
                    {
                        copy.SetData(format, autoConvert: false, CloneClipboardValue(value));
                    }
                }
                catch
                {
                    // A clipboard owner may advertise a format that it
                    // cannot render. Preserve all formats that can be read.
                }
            }

            return new ClipboardSnapshot(copy);
        });
    }

    public void Restore(int retryCount, TimeSpan retryDelay)
    {
        RunInSta(() =>
        {
            if (_data is null)
            {
                RetryClipboard(() => { Clipboard.Clear(); return true; }, retryCount, retryDelay);
                return true;
            }

            RetryClipboard(() => { Clipboard.SetDataObject(_data, copy: true); return true; }, retryCount, retryDelay);
            return true;
        });
    }

    public static string? ReadUnicodeText(int retryCount, TimeSpan retryDelay)
    {
        return RunInSta(() => RetryClipboard(() =>
        {
            if (!Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                return null;
            }

            return Clipboard.GetText(TextDataFormat.UnicodeText);
        }, retryCount, retryDelay));
    }

    public static void WriteUnicodeText(string text, int retryCount, TimeSpan retryDelay)
    {
        ArgumentNullException.ThrowIfNull(text);

        RunInSta(() => RetryClipboard(() =>
        {
            var dataObject = new DataObject();
            dataObject.SetText(text, TextDataFormat.UnicodeText);

            // copy: true keeps the text on the clipboard after the plugin process exits.
            Clipboard.SetDataObject(dataObject, copy: true);
            return true;
        }, retryCount, retryDelay));
    }

    private static object CloneClipboardValue(object value)
    {
        return value switch
        {
            byte[] bytes => bytes.ToArray(),
            MemoryStream stream => new MemoryStream(stream.ToArray(), writable: false),
            ICloneable cloneable => cloneable.Clone() ?? value,
            _ => value
        };
    }

    private static T RetryClipboard<T>(Func<T> operation, int retryCount, TimeSpan retryDelay)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt < retryCount; attempt++)
        {
            try
            {
                return operation();
            }
            catch (ExternalException ex)
            {
                lastException = ex;
                Thread.Sleep(retryDelay);
            }
        }

        throw new InvalidOperationException("The Windows clipboard remained unavailable.", lastException);
    }

    private static T RunInSta<T>(Func<T> operation)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return operation();
        }

        T? result = default;
        Exception? error = null;

        using var completed = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            try
            {
                result = operation();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                completed.Set();
            }
        })
        {
            IsBackground = true,
            Name = "DoriDeck Clipboard STA"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        completed.Wait();

        if (error is not null)
        {
            throw error;
        }

        return result!;
    }
}
