namespace Telegram.Bot.Enums
{
    public static class CallbackResponses
    {
        public const string CreateCompany = "create_company";
        public const string EditCompany = "edit_company";
        public const string AddService = "add_service";
        public const string EditService = "edit_service_{0}";
        public const string DeleteService = "delete_service_{0}";
        public const string SetupWorkDays = "setup_workdays";
        public const string SetupWorkTime = "setup_worktime";
        public const string SetupTimeSlots = "setup_timeslots";
        public const string ChangeLanguage = "change_language";
        public const string ViewProfile = "view_profile";
        public const string SetInterval = "set_interval_{0}"; // {0} = minutes
        public const string GenerateInviteLink = "generate_invite_link";
        public const string BackToMenu = "back_to_menu";
        public const string Ignore = "ignore";
        public const string BookAppointment = "book_appointment";
        public const string ViewBookings = "view_bookings";
        public const string ChangeTimezone = "change_timezone";
        public const string BackToServices = "back_to_services";
        public const string ConfirmBooking = "confirm_booking";
        public const string RejectBooking = "reject_booking";
        public const string InitWorkTime = "init_work_time";
    }
}
