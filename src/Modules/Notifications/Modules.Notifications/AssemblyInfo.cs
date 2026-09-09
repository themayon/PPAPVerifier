using System.Runtime.CompilerServices;
using DES.Framework.Web.Modules;

[assembly: DesModule(typeof(DES.Modules.Notifications.NotificationsModule), 750)]
[assembly: InternalsVisibleTo("Notifications.Tests")]
[assembly: InternalsVisibleTo("Integration.Tests")]
