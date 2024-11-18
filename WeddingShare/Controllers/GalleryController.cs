using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using WeddingShare.Extensions;
using WeddingShare.Helpers;
using WeddingShare.Models;

namespace WeddingShare.Controllers
{
    public class GalleryController : Controller
    {
        private readonly IWebHostEnvironment _hostingEnvironment;
        private readonly IConfigHelper _config;
        private readonly ILogger _logger;
     
        private readonly string UploadsDirectory;

        public GalleryController(IWebHostEnvironment hostingEnvironment, IConfigHelper config, ILogger<GalleryController> logger)
        {
            _hostingEnvironment = hostingEnvironment;
            _config = config;
            _logger = logger;

            UploadsDirectory = Path.Combine(_hostingEnvironment.WebRootPath, "uploads");
        }

        public IActionResult Index(string id, string? key)
        {
            id = id.ToLower();

            var secretKey = _config.Get("Settings", "Secret_Key");
            if (!string.IsNullOrEmpty(secretKey) && !string.Equals(secretKey, key))
            {
                _logger.LogWarning("A request was made using an invalid security hey");
                ViewBag.ErrorMessage = "Invalid gallery key";

                return View("~/Views/Home/Index.cshtml");
            }
            else if (string.IsNullOrEmpty(id))
            {
                ViewBag.ErrorMessage = "Invalid gallery id";

                return View("~/Views/Home/Index.cshtml");
            }

            ViewBag.SecretKey = key;

            var galleryPath = Path.Combine(UploadsDirectory, id);
            var allowedFileTypes = _config.GetOrDefault("Settings", "Allowed_File_Types", ".jpg,.jpeg,.png").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var files = Directory.Exists(galleryPath) ? Directory.GetFiles(galleryPath, "*.*", SearchOption.TopDirectoryOnly)?.Where(x => allowedFileTypes.Any(y => string.Equals(Path.GetExtension(x).Trim('.'), y.Trim('.'), StringComparison.OrdinalIgnoreCase))) : null;
            var images = new PhotoGallery(_config.GetOrDefault("Settings", "Gallery_Columns", 4))
            {
                GalleryId = id,
                GalleryPath = $"/{galleryPath.Remove(_hostingEnvironment.WebRootPath).Replace('\\', '/').TrimStart('/')}",
                Images = files?.OrderByDescending(x => new FileInfo(x).CreationTimeUtc)?.Select(x => Path.GetFileName(x))?.ToList(),
                FileUploader = new FileUploader(id, "/Gallery/UploadImage")
            };

            return View(images);
        }

        public async Task<IActionResult> UploadImage()
        {
            try
            {
                var secretKey = _config.Get("Settings", "Secret_Key");
                var key = Request?.Form?.FirstOrDefault(x => string.Equals("SecretKey", x.Key, StringComparison.OrdinalIgnoreCase)).Value;
                if (!string.IsNullOrEmpty(secretKey) && !string.Equals(secretKey, key))
                {
                    _logger.LogWarning("A request was made using an invalid security hey");
                    throw new UnauthorizedAccessException("The provided access token was invalid");
                }

                string galleryId = Request?.Form?.FirstOrDefault(x => string.Equals("GalleryId", x.Key, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;
                if (string.IsNullOrEmpty(galleryId))
                {
                    return Json(new { success = true, uploaded = 0, errors = new List<string>() { "Invalid gallery Id detected" } });
                }

                galleryId = galleryId.ToLower();

                var galleryPath = Path.Combine(UploadsDirectory, galleryId);
                var files = Request?.Form?.Files;
                if (files != null && files.Count > 0)
                {
                    if (!Directory.Exists(galleryPath))
                    {
                        Directory.CreateDirectory(galleryPath);
                    }

                    var uploaded = 0;
                    var errors = new List<string>();
                    foreach (IFormFile file in files)
                    {
                        try
                        {
                            var extension = Path.GetExtension(file.FileName);
                            var maxFilesSize = _config.GetOrDefault("Settings", "Max_File_Size_Mb", 10) * 1000000;

                            var allowedFileTypes = _config.GetOrDefault("Settings", "Allowed_File_Types", ".jpg,.jpeg,.png").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                            if (!allowedFileTypes.Any(x => string.Equals(x.Trim('.'), extension.Trim('.'), StringComparison.OrdinalIgnoreCase)))
                            {
                                errors.Add($"Failed to upload file '{Path.GetFileName(file.FileName)}'. File type is invalid");
                            }
                            else if (file.Length > maxFilesSize)
                            {
                                errors.Add($"Failed to upload file '{Path.GetFileName(file.FileName)}'. Max file size is {maxFilesSize} bytes");
                            }
                            else
                            {
                                var filePath = Path.Combine(galleryPath, $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}");
                                if (!string.IsNullOrEmpty(filePath))
                                {
                                    using (var fs = new FileStream(filePath, FileMode.Create))
                                    {
                                        await file.CopyToAsync(fs);
                                        uploaded++;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"Failed to save image to gallery - {ex?.Message}");
                        }
                    }

                    return Json(new { success = true, uploaded, errors });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to upload images - {ex?.Message}");
            }

            return Json(new { success = false, uploaded = 0 });
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}