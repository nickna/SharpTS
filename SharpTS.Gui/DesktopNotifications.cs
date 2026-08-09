using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace SharpTS.Gui;

internal static partial class DesktopNotifications
{
    private const int MaximumTitleLength = 256;
    private const int MaximumMessageLength = 4096;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorNoPackage = 15_700;
    private const int RpcEChangedMode = unchecked((int)0x80010106);

    private static readonly Guid IidXmlDocumentIo = new("6cd0e74e-ee65-4489-9ebf-ca43e87ba637");
    private static readonly Guid IidToastNotificationFactory = new("04124b20-82c6-4229-b109-fd9ed4662b53");
    private static readonly Guid IidToastNotificationManagerStatics = new("50ac103f-d235-4598-bbef-98fe4d1a3ad4");

    public static Task ShowAsync(bool headless, string title, string message, bool silent)
    {
        string xml = CreateToastXml(title, message, silent);
        if (headless)
            return Task.CompletedTask;
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "showNotification is supported only by installed Windows MSIX applications.");
        if (!HasPackageIdentity())
            throw new InvalidOperationException(
                "Windows notifications require the application to be installed and launched from its SharpTS MSIX package identity.");

        try
        {
            ShowNative(xml);
            return Task.CompletedTask;
        }
        catch (COMException exception)
        {
            throw new InvalidOperationException(
                $"Windows could not show the notification ({FormatHResult(exception.HResult)}).",
                exception);
        }
    }

    internal static string CreateToastXml(string title, string message, bool silent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(message);
        if (title.Length > MaximumTitleLength)
            throw new ArgumentOutOfRangeException(nameof(title), $"Notification titles cannot exceed {MaximumTitleLength} characters.");
        if (message.Length > MaximumMessageLength)
            throw new ArgumentOutOfRangeException(nameof(message), $"Notification messages cannot exceed {MaximumMessageLength} characters.");

        var binding = new XElement("binding",
            new XAttribute("template", "ToastGeneric"),
            new XElement("text", title));
        if (message.Length != 0)
            binding.Add(new XElement("text", message));
        var toast = new XElement("toast", new XElement("visual", binding));
        if (silent)
            toast.Add(new XElement("audio", new XAttribute("silent", "true")));
        return toast.ToString(SaveOptions.DisableFormatting);
    }

    internal static unsafe bool HasPackageIdentity()
    {
        uint length = 0;
        int result = GetCurrentPackageFullName(ref length, null);
        return result switch
        {
            ErrorInsufficientBuffer => true,
            ErrorNoPackage => false,
            _ => throw new InvalidOperationException(
                $"Windows package identity detection failed with error {result} (0x{result:x8})."),
        };
    }

    private static unsafe void ShowNative(string xml)
    {
        int initialization = RoInitialize(1);
        if (initialization < 0 && initialization != RpcEChangedMode)
            ThrowForHResult(initialization, "RoInitialize");
        bool uninitialize = initialization >= 0;

        nint document = 0;
        nint documentIo = 0;
        nint manager = 0;
        nint notifier = 0;
        nint notificationFactory = 0;
        nint notification = 0;
        try
        {
            document = ActivateInstance("Windows.Data.Xml.Dom.XmlDocument");
            documentIo = QueryInterface(document, IidXmlDocumentIo);
            using (var content = new OwnedHString(xml))
            {
                var loadXml = (delegate* unmanaged[Stdcall]<nint, nint, int>)GetVtable(documentIo)[6];
                ThrowForHResult(loadXml(documentIo, content.Value), "IXmlDocumentIO.LoadXml");
            }

            manager = GetActivationFactory(
                "Windows.UI.Notifications.ToastNotificationManager",
                IidToastNotificationManagerStatics);
            var createNotifier = (delegate* unmanaged[Stdcall]<nint, nint*, int>)GetVtable(manager)[6];
            ThrowForHResult(createNotifier(manager, &notifier),
                "IToastNotificationManagerStatics.CreateToastNotifier");

            notificationFactory = GetActivationFactory(
                "Windows.UI.Notifications.ToastNotification",
                IidToastNotificationFactory);
            var createNotification = (delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)GetVtable(notificationFactory)[6];
            ThrowForHResult(createNotification(notificationFactory, document, &notification),
                "IToastNotificationFactory.CreateToastNotification");

            var show = (delegate* unmanaged[Stdcall]<nint, nint, int>)GetVtable(notifier)[6];
            ThrowForHResult(show(notifier, notification), "IToastNotifier.Show");
        }
        finally
        {
            Release(notification);
            Release(notificationFactory);
            Release(notifier);
            Release(manager);
            Release(documentIo);
            Release(document);
            if (uninitialize)
                RoUninitialize();
        }
    }

    private static unsafe nint ActivateInstance(string runtimeClass)
    {
        using var className = new OwnedHString(runtimeClass);
        nint instance = 0;
        ThrowForHResult(RoActivateInstance(className.Value, &instance),
            $"RoActivateInstance({runtimeClass})");
        return instance;
    }

    private static unsafe nint GetActivationFactory(string runtimeClass, Guid iid)
    {
        using var className = new OwnedHString(runtimeClass);
        nint factory = 0;
        ThrowForHResult(RoGetActivationFactory(className.Value, &iid, &factory),
            $"RoGetActivationFactory({runtimeClass})");
        return factory;
    }

    private static unsafe nint QueryInterface(nint instance, Guid iid)
    {
        nint result = 0;
        var queryInterface = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)GetVtable(instance)[0];
        ThrowForHResult(queryInterface(instance, &iid, &result), "IUnknown.QueryInterface");
        return result;
    }

    private static unsafe nint* GetVtable(nint instance) => *(nint**)instance;

    private static unsafe void Release(nint instance)
    {
        if (instance == 0)
            return;
        var release = (delegate* unmanaged[Stdcall]<nint, uint>)GetVtable(instance)[2];
        _ = release(instance);
    }

    private static void ThrowForHResult(int hresult, string operation)
    {
        if (hresult < 0)
            throw new COMException($"{operation} failed with {FormatHResult(hresult)}.", hresult);
    }

    private static string FormatHResult(int hresult) => $"HRESULT 0x{unchecked((uint)hresult):x8}";

    private readonly unsafe ref struct OwnedHString
    {
        public OwnedHString(string value)
        {
            nint result = 0;
            fixed (char* valuePointer = value)
                ThrowForHResult(WindowsCreateString(valuePointer, checked((uint)value.Length), &result),
                    "WindowsCreateString");
            Value = result;
        }

        public nint Value { get; }

        public void Dispose()
        {
            if (Value != 0)
                _ = WindowsDeleteString(Value);
        }
    }

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetCurrentPackageFullName(ref uint packageFullNameLength, char* packageFullName);

    [LibraryImport("combase.dll")]
    private static partial int RoInitialize(uint initType);

    [LibraryImport("combase.dll")]
    private static partial void RoUninitialize();

    [LibraryImport("combase.dll")]
    private static unsafe partial int RoActivateInstance(nint activatableClassId, nint* instance);

    [LibraryImport("combase.dll")]
    private static unsafe partial int RoGetActivationFactory(nint activatableClassId, Guid* iid, nint* factory);

    [LibraryImport("combase.dll")]
    private static unsafe partial int WindowsCreateString(char* sourceString, uint length, nint* value);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(nint value);
}
