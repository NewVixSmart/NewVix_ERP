using NewVixSmart.Web.Services;

namespace NewVixSmart.Web.ViewModels.Users;

public class UserListItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public int PermissionCount { get; set; }
}

public class UserPermissionViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public bool IsAdmin { get; set; }
    public List<ModulePermissionViewModel> Modules { get; set; } = new();
}

public class ModulePermissionViewModel
{
    public string Key { get; set; } = string.Empty;
    public string TitleAr { get; set; } = string.Empty;
    public bool View;
    public bool Create;
    public bool Edit;
    public bool Delete;

    public bool HasAction(string action) =>
        PermissionCatalog.Key(Key, action) switch
        {
            _ when action == PermissionCatalog.View && View => true,
            _ when action == PermissionCatalog.Create && Create => true,
            _ when action == PermissionCatalog.Edit && Edit => true,
            _ when action == PermissionCatalog.Delete && Delete => true,
            _ => false
        };
}

public class CreateUserViewModel
{
    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "اسم المستخدم مطلوب")]
    [System.ComponentModel.DataAnnotations.StringLength(100)]
    public string Username { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "كلمة المرور مطلوبة")]
    [System.ComponentModel.DataAnnotations.StringLength(100, MinimumLength = 6, ErrorMessage = "كلمة المرور 6 أحرف على الأقل")]
    public string Password { get; set; } = string.Empty;

    public string Role { get; set; } = "Warehouse";
}