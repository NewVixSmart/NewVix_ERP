using NewVixSmart.Web.Services;

namespace NewVixSmart.Web.ViewModels.Users;

public class UserListItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public int PermissionCount { get; set; }
    public bool IsDeactivated { get; set; }

    public bool IsAdmin => Roles.Contains(UserRoleLabels.Admin);

    /// <summary>
    /// The roles worth listing as a badge. "مدير النظام" already has a badge of its own in the user
    /// name cell, so the Admin role is not repeated here; the view used to filter it out inline.
    /// </summary>
    public List<string> DisplayRoles => Roles.Where(r => r != UserRoleLabels.Admin).ToList();
}

/// <summary>
/// The role keys the seed and the permission catalogue agree on, next to their Arabic labels. The
/// user list used to map a role key to its Arabic word with a nested ternary inside the markup, which
/// meant every key that was not "Accountant" - including one nobody had added yet - was silently
/// labelled "أمين مخزن". A role outside the catalogue now renders as an unnamed role instead of as
/// somebody else's job title; its key is still visible on the permissions screen.
/// </summary>
public static class UserRoleLabels
{
    public const string Admin = "Admin";
    public const string Accountant = "Accountant";
    public const string Warehouse = "Warehouse";

    public const string UnknownRole = "دور غير محدد";

    public static string DisplayName(string role) => role switch
    {
        Admin => "مدير النظام",
        Accountant => "محاسب",
        Warehouse => "أمين مخزن",
        _ => UnknownRole
    };
}

public class UserPermissionViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public bool IsAdmin { get; set; }
    public List<ModulePermissionViewModel> Modules { get; set; } = new();

    /// <summary>
    /// مفاتيحُ الصلاحيات كما رُسمت للمستخدم، مُفصولةٌ بفواصل ومرتّبة. تُعاد مع النموذج
    /// فيقارنها الخادمُ بالمحفوظ عند الحفظ، فترفض كلَّه إن اختلفت — وهذا هو الحارسُ
    /// الممكن هنا: لا <c>RowVersion</c> على <c>UserPermission</c> يحمي شيئًا، لأنّ الحفظ
    /// يحذفُ كلَّ الصفوف ويعيد بناءها، فلا يبقى صفٌّ قديمٌ يُرافَق برمزه.
    /// </summary>
    public string RenderedKeys { get; set; } = string.Empty;
}

public class ModulePermissionViewModel
{
    public string Key { get; set; } = string.Empty;
    public string TitleAr { get; set; } = string.Empty;
    public bool View;
    public bool Create;
    public bool Edit;
    public bool Delete;
    public HashSet<string> Granted { get; set; } = new();

    public bool HasAction(string action) => Granted.Contains(PermissionCatalog.Key(Key, action));
}

public class CreateUserViewModel
{
    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "اسم المستخدم مطلوب")]
    [System.ComponentModel.DataAnnotations.StringLength(100)]
    public string Username { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "كلمة المرور مطلوبة")]
    [System.ComponentModel.DataAnnotations.StringLength(100, MinimumLength = 8, ErrorMessage = "كلمة المرور 8 أحرف على الأقل (مع حرف كبير وصغير ورقم ورمز)")]
    public string Password { get; set; } = string.Empty;

    public string Role { get; set; } = "Warehouse";
}
