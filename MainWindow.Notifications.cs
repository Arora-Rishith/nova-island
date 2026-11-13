using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace NovaIsland;

public partial class MainWindow
{
    private sealed record NotificationItem(string Id, string App, string Title, string Body, DateTimeOffset Time);
    private readonly List<NotificationItem> localNotifications = new();
    private IReadOnlyList<NotificationItem> windowsNotifications = Array.Empty<NotificationItem>();
    private string notificationStatus = "Enable Windows notification access to see your notification centre.";
    private bool notificationAccess;
    private bool refreshingNotifications;

    private static bool HasPackageIdentity
    {
        get { try { return !string.IsNullOrEmpty(Windows.ApplicationModel.Package.Current.Id.Name); } catch { return false; } }
    }

    private void AddNotice(string title, string body)
    {
        if (localNotifications.FirstOrDefault() is { } previous && previous.Title == title && previous.Body == body) return;
        localNotifications.Insert(0, new("nova:" + Guid.NewGuid().ToString("N"), "NOVA", title, body, DateTimeOffset.Now));
        if (localNotifications.Count > 50) localNotifications.RemoveAt(localNotifications.Count - 1);
    }

    private async Task EnableNotificationsAsync()
    {
        if (!HasPackageIdentity)
            throw new InvalidOperationException("Windows notification access requires the installed MSIX package. NOVA notices are available now.");
        var access = await UserNotificationListener.Current.RequestAccessAsync();
        if (access != UserNotificationListenerAccessStatus.Allowed)
            throw new InvalidOperationException("Notification access: " + access + ". You can change this in Windows Settings.");
        await RefreshNotificationsAsync();
    }

    private async Task RefreshNotificationsAsync()
    {
        if (refreshingNotifications || isClosed) return;
        refreshingNotifications = true;
        try
        {
            notificationAccess = HasPackageIdentity && UserNotificationListener.Current.GetAccessStatus() == UserNotificationListenerAccessStatus.Allowed;
            if (!notificationAccess)
            {
                windowsNotifications = Array.Empty<NotificationItem>();
                notificationStatus = HasPackageIdentity ? "Enable Windows notification access." : "NOVA notices · Install MSIX to enable Windows notifications.";
                return;
            }
            var notifications = await UserNotificationListener.Current.GetNotificationsAsync(NotificationKinds.Toast);
            var items = new List<NotificationItem>();
            foreach (var notification in notifications)
            {
                try
                {
                    var text = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements();
                    if (text == null) continue;
                    items.Add(new("windows:" + notification.Id, notification.AppInfo.DisplayInfo.DisplayName,
                        text.FirstOrDefault()?.Text ?? "Notification", string.Join("\n", text.Skip(1).Select(t => t.Text)), notification.CreationTime));
                }
                catch (Exception) { /* A notification can be removed by its app while enumerating. */ }
            }
            windowsNotifications = items.OrderByDescending(n => n.Time).ToArray();
            notificationStatus = "Windows notification centre · Live";
        }
        catch (Exception ex)
        {
            windowsNotifications = Array.Empty<NotificationItem>();
            notificationAccess = false;
            notificationStatus = "Notification access unavailable: " + ex.Message;
        }
        finally { refreshingNotifications = false; }
    }

    private async Task DismissNotificationAsync(string id)
    {
        if (id.StartsWith("nova:", StringComparison.Ordinal)) localNotifications.RemoveAll(n => n.Id == id);
        else if (windowsNotifications.Any(n => n.Id == id) && uint.TryParse(id[8..], out uint nativeId))
        {
            if (UserNotificationListener.Current.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
                throw new InvalidOperationException("Windows notification permission has been revoked.");
            UserNotificationListener.Current.RemoveNotification(nativeId);
        }
        else throw new ArgumentException("Notification no longer exists.");
        await RefreshNotificationsAsync();
    }

    private async Task ClearVisibleNotificationsAsync()
    {
        // Remove exactly the notifications shown, including any that arrived after the last snapshot only on a later clear.
        foreach (string id in windowsNotifications.Select(n => n.Id).ToArray()) await DismissNotificationAsync(id);
        localNotifications.Clear();
    }
}
