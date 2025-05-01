
using Telegram.Bot.Enums;

namespace Telegram.Bot.Commands.Helpers;
public static class RoleHandler
{
    public static bool IsClient(this UserRole roles) => HasRole(roles, UserRole.Client);
    public static bool IsCompany(this UserRole roles) => HasRole(roles, UserRole.Company);
    public static bool IsUnknown(this UserRole roles) => HasRole(roles, UserRole.Unknown);

    public static bool IsClientOrCompany(this UserRole roles) =>
        HasRole(roles, UserRole.Client | UserRole.Company);
    public static bool HasRole(UserRole roles, UserRole roleToCheck) =>
        (roles & roleToCheck) == roleToCheck;
}