using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Access;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Users;

namespace NewVixSmart.Web.Controllers;

[Authorize(Roles = "Admin")]
public class UsersController : Controller
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly AppDbContext _db;

    public UsersController(UserManager<IdentityUser> userManager, AppDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var users = await _userManager.Users.AsNoTracking().ToListAsync();
        var userIds = users.Select(u => u.Id).ToArray();
        var permsByUser = await _db.UserPermissions.AsNoTracking().GroupBy(p => p.UserId)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
        var rolesByUser = await _db.UserRoles.AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId))
            .Join(_db.Roles.AsNoTracking(), ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .GroupBy(x => x.UserId)
            .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.Name!).ToList());

        var items = new List<UserListItemViewModel>();
        foreach (var user in users)
        {
            items.Add(new UserListItemViewModel
            {
                Id = user.Id,
                UserName = user.UserName ?? user.Id,
                Roles = rolesByUser.TryGetValue(user.Id, out var roles) ? roles : new List<string>(),
                PermissionCount = permsByUser.TryGetValue(user.Id, out var c) ? c : 0,
                IsDeactivated = await _userManager.IsLockedOutAsync(user)
            });
        }
        return View(items.OrderByDescending(x => x.Roles.Contains("Admin")).ThenBy(x => x.UserName).ToList());
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Roles = new SelectList(new[] { "Accountant", "Warehouse", "Admin" }, "Warehouse");
        return View(new CreateUserViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel vm)
    {
        var role = vm.Role ?? "Warehouse";
        if (role != "Admin" && role != "Accountant" && role != "Warehouse")
        {
            ModelState.AddModelError(nameof(CreateUserViewModel.Role), "الدور غير صالح");
        }

        if (ModelState.IsValid)
        {
            var existing = await _userManager.FindByNameAsync(vm.Username);
            if (existing != null)
            {
                ModelState.AddModelError(nameof(CreateUserViewModel.Username), "اسم المستخدم مستخدم بالفعل");
            }
            else
            {
                var user = new IdentityUser { UserName = vm.Username, Email = $"{vm.Username}@vix.local", EmailConfirmed = false };
                var result = await _userManager.CreateAsync(user, vm.Password);
                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(user, role);
                    if (role != "Admin")
                    {
                        _db.UserPermissions.AddRange(PermissionDefaults.DefaultsFor(role)
                            .Select(k => new UserPermission { UserId = user.Id, PermissionKey = k }));
                        await _db.SaveChangesAsync();
                    }
                    TempData["Success"] = "تم إنشاء المستخدم بنجاح";
                    return RedirectToAction(nameof(Permissions), new { id = user.Id });
                }
                foreach (var err in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, err.Description);
                }
            }
        }

        ViewBag.Roles = new SelectList(new[] { "Accountant", "Warehouse", "Admin" }, vm.Role);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleDeactivated(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }

        // No admin can revoke their own access here: the only other route to recovery is
        // another admin, and a single-admin deployment would then be locked out for good.
        if (user.Id == _userManager.GetUserId(User))
        {
            TempData["Error"] = "لا يمكنك إبطال حسابك بنفسك";
            return RedirectToAction(nameof(Index));
        }

        // Lockout rather than deletion: audit trails and historical documents keep
        // CreatedBy as free text, but the account row must stay for referential history.
        // Both login paths already honour LockoutEnd - SignInAsync(lockoutOnFailure: true)
        // in AccountController and IsLockedOutAsync in TokensController - so this is the
        // enforcement point the codebase already trusts, not a new mechanism.
        var isLocked = await _userManager.IsLockedOutAsync(user);
        var result = isLocked
            ? await _userManager.SetLockoutEndDateAsync(user, null)
            : await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join("، ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = isLocked
            ? $"أُعيد تفعيل «{user.UserName}»"
            : $"أُبطل حساب «{user.UserName}» — لن يستطيع تسجيل الدخول";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Permissions(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }

        var roles = (await _userManager.GetRolesAsync(user)).ToList();
        var granted = (await _db.UserPermissions.AsNoTracking()
            .Where(p => p.UserId == user.Id)
            .Select(p => p.PermissionKey)
            .ToListAsync()).ToHashSet();

        var modules = PermissionCatalog.Modules.Select(m =>
        {
            var acts = PermissionCatalog.ActionsFor(m.Key);
            return new ModulePermissionViewModel
            {
                Key = m.Key,
                TitleAr = m.TitleAr,
                Granted = granted,
                View = granted.Contains(PermissionCatalog.Key(m.Key, PermissionCatalog.View)),
                Create = granted.Contains(PermissionCatalog.Key(m.Key, PermissionCatalog.Create)),
                Edit = granted.Contains(PermissionCatalog.Key(m.Key, PermissionCatalog.Edit)),
                Delete = granted.Contains(PermissionCatalog.Key(m.Key, PermissionCatalog.Delete))
            };
        }).ToList();

        return View(new UserPermissionViewModel
        {
            UserId = user.Id,
            UserName = user.UserName ?? user.Id,
            Roles = roles,
            IsAdmin = roles.Contains("Admin"),
            Modules = modules
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Permissions(string id, string[] perm)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }

        if ((await _userManager.GetRolesAsync(user)).Contains("Admin"))
        {
            TempData["Error"] = "صلاحيات المدير لا تُعدَّل — يملك كل الصلاحيات تلقائيًا";
            return RedirectToAction(nameof(Permissions), new { id });
        }

        var validKeys = new HashSet<string>(
            PermissionCatalog.Modules.SelectMany(m => PermissionCatalog.ActionsFor(m.Key)
                .Select(a => PermissionCatalog.Key(m.Key, a))));

        var selected = (perm ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Where(p => validKeys.Contains(p))
            .Distinct()
            .ToHashSet();

        // الرؤية إلزامية إذا مُنح أي إجراء في الوحدة
        foreach (var module in PermissionCatalog.Modules)
        {
            var viewKey = PermissionCatalog.Key(module.Key, PermissionCatalog.View);
            var moduleActions = selected.Where(x => x.StartsWith(module.Key + ".")).Select(x => x[(module.Key.Length + 1)..]).ToList();
            if (moduleActions.Count > 0 && !selected.Contains(viewKey))
            {
                selected.Add(viewKey);
            }
        }

        var existing = await _db.UserPermissions.Where(p => p.UserId == user.Id).ToListAsync();
        _db.UserPermissions.RemoveRange(existing);
        _db.UserPermissions.AddRange(selected.Select(k => new UserPermission
        {
            UserId = user.Id,
            PermissionKey = k
        }));
        await _db.SaveChangesAsync();

        TempData["Success"] = $"تم تحديث صلاحيات «{user.UserName}»";
        return RedirectToAction(nameof(Index));
    }
}
