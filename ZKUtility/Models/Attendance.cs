namespace ZKUtility.Models
{
    public class Attendance
    {
        public int Id { get; set; }

        public int EnrollNumber { get; set; }
        public DateTime Date { get; set; }

        public DateTime? CheckIn { get; set; }
        public DateTime? CheckOut { get; set; }

        public int? CheckInMode { get; set; }
        public int? CheckOutMode { get; set; }

    }
}