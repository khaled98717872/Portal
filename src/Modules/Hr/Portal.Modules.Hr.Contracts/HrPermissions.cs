namespace Portal.Modules.Hr.Contracts;

public static class HrPermissions
{
    public static class Employees
    {
        public const string Read = "hr.employees.read";
        public const string Create = "hr.employees.create";
        public const string Update = "hr.employees.update";
        public const string Delete = "hr.employees.delete";
    }

    public static class Audit
    {
        public const string Read = "hr.audit.read";
    }
}
