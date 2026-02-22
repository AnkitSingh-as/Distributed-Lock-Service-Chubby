using System;
using Chubby.Core.Rpc;

namespace Chubby.Core.Utils;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class RequireAttribute : Attribute
{
    public Permission RequiredPermission { get; }

    public RequireAttribute(Permission permission)
    {
        this.RequiredPermission = permission;
    }
}
