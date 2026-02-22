namespace Chubby.Core.Rpc
{
    public static class PermissionIntentMapping
    {
        public static Dictionary<Intent, Permission> mapping =
        new(){
            { Intent.Read, Permission.Read },
            { Intent.Write, Permission.Write },
            { Intent.SharedLock, Permission.Read },
            { Intent.ExclusiveLock, Permission.Write },
            { Intent.ChangeAcl, Permission.ChangeAcl },
        };

        public static Permission GetPermission(Intent intent)
        {
            return mapping[intent];
        }

    }
}