using System.ComponentModel.DataAnnotations;

namespace NewVixSmart.Web.Models.Access;

public class UserPermission
{
    public int Id { get; set; }

    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(64)]
    public string PermissionKey { get; set; } = string.Empty;

    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
}
