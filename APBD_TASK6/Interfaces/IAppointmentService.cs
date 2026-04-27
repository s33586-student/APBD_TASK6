using Microsoft.AspNetCore.Mvc;

namespace APBD_TASK6.Interfaces
{
    public interface IAppointmentService
    {
        public Task<(bool ok, IActionResult? error)> ValidateDoctorAndPatient(int doctorId, int patientId);
        public Task<(bool ok, IActionResult? error)> ValidateDoctorDateConflict(int doctorId, DateTime appointmentDate);
        public Task<(bool ok, IActionResult? error)> ValidateDoctorDateWithAppointmentId(int doctorId, DateTime appointmentDate, int id);
        public Task<(bool ok, IActionResult? error)> ValidateAppointmentCompletionDateChange(int id, DateTime appointmentDate);
        public Task<(bool ok, IActionResult? error)> ValidateAppointmentCompletion(int id);
    }
}

