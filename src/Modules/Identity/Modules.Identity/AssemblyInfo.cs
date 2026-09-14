using DES.Framework.Web.Modules;
using System.Runtime.CompilerServices;

[assembly: DesModule(typeof(DES.Modules.Identity.IdentityModule), 100)]
[assembly: InternalsVisibleTo("Identity.Tests")]