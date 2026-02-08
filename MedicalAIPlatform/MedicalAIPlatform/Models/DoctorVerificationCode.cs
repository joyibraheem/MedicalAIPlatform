using System;
using System.ComponentModel.DataAnnotations;

namespace MedicalAIPlatform.Models
{
    public class DoctorVerificationCode
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string Code { get; set; } = string.Empty;

        public bool IsUsed { get; set; } = false;

        [StringLength(450)]
        public string? UsedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UsedAt { get; set; }
    }
}
