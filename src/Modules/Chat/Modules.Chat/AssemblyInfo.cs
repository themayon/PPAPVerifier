using System.Runtime.CompilerServices;
using DES.Framework.Web.Modules;

[assembly: DesModule(typeof(DES.Modules.Chat.ChatModule), 800)]
[assembly: InternalsVisibleTo("Chat.Tests")]
[assembly: InternalsVisibleTo("Integration.Tests")]
