using System.Data.Common;
using Microsoft.AspNetCore.Authorization;
using FitnessApp.DTO;
using FitnessApp.Service;
using FitnessApp.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Controllers;

[AllowAnonymous]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class AccountController(AuthService authService, ILogger<AccountController> logger) : Controller
{
    [HttpGet]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectAfterAuthentication(returnUrl);

        return View(new LoginViewModel { ReturnUrl = GetLocalReturnUrl(returnUrl) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        model.ReturnUrl = GetLocalReturnUrl(model.ReturnUrl);
        if (!ModelState.IsValid)
            return View(model);

        try
        {
            var result = await authService.Login(
                new LoginRequest(model.Email, model.Password), cancellationToken);
            if (result.IsSuccess)
            {
                TempData["Success"] = "You are signed in.";
                return RedirectAfterAuthentication(model.ReturnUrl);
            }

            ModelState.AddModelError(string.Empty, result.Error ?? "Unable to sign in. Please try again.");
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            logger.LogError(exception, "Unable to complete MVC sign-in.");
            ModelState.AddModelError(string.Empty,
                "Sign-in is temporarily unavailable. Please try again shortly.");
        }

        return View(model);
    }

    [HttpGet]
    public IActionResult Register(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectAfterAuthentication(returnUrl);

        return View(new RegisterViewModel { ReturnUrl = GetLocalReturnUrl(returnUrl) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken)
    {
        model.ReturnUrl = GetLocalReturnUrl(model.ReturnUrl);
        if (!ModelState.IsValid)
            return View(model);

        try
        {
            var result = await authService.CreateUser(new RegisterRequest
            {
                Username = model.FullName,
                Email = model.Email,
                Dateofbirth = model.DateOfBirth!.Value,
                Password = model.Password
            }, cancellationToken);

            if (result.IsSuccess)
            {
                TempData["Success"] = "Your account is ready. You are signed in.";
                return RedirectAfterAuthentication(model.ReturnUrl);
            }

            ModelState.AddModelError(string.Empty, result.Error ?? "Unable to create your account. Please try again.");
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            logger.LogError(exception, "Unable to complete MVC registration.");
            ModelState.AddModelError(string.Empty,
                "Registration is temporarily unavailable. Please try again shortly.");
        }

        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        try
        {
            await authService.Logout(cancellationToken);
            TempData["Success"] = "You are signed out.";
            return RedirectToAction(nameof(Login));
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            logger.LogError(exception, "Unable to complete MVC sign-out.");
            TempData["Error"] = "We couldn't sign you out. Please try again shortly.";
            return RedirectToAction("Index", "Home");
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Refresh(string? returnUrl, CancellationToken cancellationToken)
    {
        try
        {
            var result = await authService.Refresh(cancellationToken);
            if (result.IsSuccess)
            {
                TempData["Success"] = "Your session has been renewed.";
                return RedirectAfterAuthentication(returnUrl);
            }

            TempData["Error"] = "Your session has expired. Please sign in again.";
            return RedirectToAction(nameof(Login), new { returnUrl = GetLocalReturnUrl(returnUrl) });
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            logger.LogError(exception, "Unable to renew the MVC session.");
            TempData["Error"] = "Session renewal is temporarily unavailable. Please try again shortly.";
            return RedirectToAction("Index", "Home");
        }
    }

    private string? GetLocalReturnUrl(string? returnUrl) =>
        Url.IsLocalUrl(returnUrl) ? returnUrl : null;

    private IActionResult RedirectAfterAuthentication(string? returnUrl) =>
        GetLocalReturnUrl(returnUrl) is { } localUrl
            ? LocalRedirect(localUrl)
            : RedirectToAction("Index", "Home");

    private static bool IsStorageFailure(Exception exception) =>
        exception is DbException or DbUpdateException or TimeoutException or IOException;
}
