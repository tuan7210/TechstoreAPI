using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechstoreBackend.Data;
using TechstoreBackend.Models;
using TechstoreBackend.Models.DTOs;
using System.Text.Json;

namespace TechstoreBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ProductController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<PagedResult<ProductResponseDto>>>> GetProducts([FromQuery] ProductQueryDto query)
        {
            try
            {
                var productsQuery = _context.Products
                    .Include(p => p.Category)
                    .AsQueryable();

                // Apply filters
                if (!string.IsNullOrEmpty(query.Search))
                {
                    productsQuery = productsQuery.Where(p => 
                        p.Name.Contains(query.Search) || 
                        p.Description.Contains(query.Search) ||
                        p.Brand.Contains(query.Search));
                }

                if (query.CategoryId.HasValue)
                {
                    productsQuery = productsQuery.Where(p => p.CategoryId == query.CategoryId.Value);
                }

                if (!string.IsNullOrEmpty(query.Brand))
                {
                    productsQuery = productsQuery.Where(p => p.Brand.Contains(query.Brand));
                }

                if (query.MinPrice.HasValue)
                {
                    productsQuery = productsQuery.Where(p => p.Price >= query.MinPrice.Value);
                }

                if (query.MaxPrice.HasValue)
                {
                    productsQuery = productsQuery.Where(p => p.Price <= query.MaxPrice.Value);
                }

                if (query.MinRating.HasValue)
                {
                    productsQuery = productsQuery.Where(p => p.Rating >= query.MinRating.Value);
                }

                if (query.IsNew.HasValue)
                {
                    productsQuery = productsQuery.Where(p => p.IsNew == query.IsNew.Value);
                }

                if (query.IsBestSeller.HasValue)
                {
                    productsQuery = productsQuery.Where(p => p.IsBestSeller == query.IsBestSeller.Value);
                }

                if (query.InStock.HasValue && query.InStock.Value)
                {
                    productsQuery = productsQuery.Where(p => p.StockQuantity > 0);
                }

                // Apply sorting
                productsQuery = query.SortBy.ToLower() switch
                {
                    "price" => query.SortOrder.ToLower() == "desc" 
                        ? productsQuery.OrderByDescending(p => p.Price)
                        : productsQuery.OrderBy(p => p.Price),
                    "rating" => query.SortOrder.ToLower() == "desc"
                        ? productsQuery.OrderByDescending(p => p.Rating)
                        : productsQuery.OrderBy(p => p.Rating),
                    "created_at" => query.SortOrder.ToLower() == "desc"
                        ? productsQuery.OrderByDescending(p => p.CreatedAt)
                        : productsQuery.OrderBy(p => p.CreatedAt),
                    _ => query.SortOrder.ToLower() == "desc"
                        ? productsQuery.OrderByDescending(p => p.Name)
                        : productsQuery.OrderBy(p => p.Name)
                };

                // Get total count
                var totalCount = await productsQuery.CountAsync();

                // Apply pagination
                var products = await productsQuery
                    .Skip((query.PageNumber - 1) * query.PageSize)
                    .Take(query.PageSize)
                    .ToListAsync();

                // Map to DTOs
                var productDtos = products.Select(p => new ProductResponseDto
                {
                    ProductId = p.ProductId,
                    Name = p.Name,
                    Description = p.Description,
                    Price = p.Price,
                    OriginalPrice = p.OriginalPrice,
                    Brand = p.Brand,
                    StockQuantity = p.StockQuantity,
                    CategoryId = p.CategoryId,
                    CategoryName = p.Category?.Name ?? "",
                    ImageUrl = p.ImageUrl,
                    Specifications = ParseSpecifications(p.Specifications),
                    Rating = p.Rating,
                    ReviewCount = p.ReviewCount,
                    IsNew = p.IsNew,
                    IsBestSeller = p.IsBestSeller,
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt
                }).ToList();

                var result = new PagedResult<ProductResponseDto>
                {
                    Items = productDtos,
                    TotalCount = totalCount,
                    PageNumber = query.PageNumber,
                    PageSize = query.PageSize
                };

                return Ok(ApiResponse<PagedResult<ProductResponseDto>>.SuccessResult(result));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<PagedResult<ProductResponseDto>>.ErrorResult(
                    "Đã xảy ra lỗi khi lấy danh sách sản phẩm", new List<string> { ex.Message }));
            }
        }

        [HttpPost("export-jsonl")]
        public async Task<ActionResult<ApiResponse<object>>> ExportProductsJsonl()
        {
            try
            {
                var products = await _context.Products
                    .Include(p => p.Category)
                    .AsNoTracking()
                    .ToListAsync();

                var lines = products.Select(p => new
                {
                    product_id = p.ProductId,
                    name = p.Name,
                    description = p.Description,
                    brand = p.Brand,
                    specifications = TryParseJson(p.Specifications),
                    price = p.Price,
                    original_price = p.OriginalPrice,
                    stock_quantity = p.StockQuantity,
                    category_id = p.CategoryId,
                    category_name = p.Category != null ? p.Category.Name : string.Empty,
                    image_url = p.ImageUrl,
                    rating = p.Rating,
                    review_count = p.ReviewCount,
                    is_new = p.IsNew,
                    is_best_seller = p.IsBestSeller,
                    created_at = p.CreatedAt,
                    updated_at = p.UpdatedAt
                }).ToList();

                var rootDir = Directory.GetCurrentDirectory();
                var outDir = Path.Combine(rootDir, "ingestion", "output");
                Directory.CreateDirectory(outDir);
                var outPath = Path.Combine(outDir, "products.jsonl");

                await using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
                await using (var sw = new StreamWriter(fs, System.Text.Encoding.UTF8))
                {
                    foreach (var item in lines)
                    {
                        var json = JsonSerializer.Serialize(item);
                        await sw.WriteLineAsync(json);
                    }
                }

                var result = new { count = lines.Count, path = outPath };
                return Ok(ApiResponse<object>.SuccessResult(result, "Xuất JSONL thành công"));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<object>.ErrorResult(
                    "Lỗi khi xuất JSONL", new List<string> { ex.Message }));
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<ProductResponseDto>>> GetProduct(int id)
        {
            try
            {
                var product = await _context.Products
                    .Include(p => p.Category)
                    .FirstOrDefaultAsync(p => p.ProductId == id);

                if (product == null)
                {
                    return NotFound(ApiResponse<ProductResponseDto>.ErrorResult("Không tìm thấy sản phẩm"));
                }

                var productDto = new ProductResponseDto
                {
                    ProductId = product.ProductId,
                    Name = product.Name,
                    Description = product.Description,
                    Price = product.Price,
                    OriginalPrice = product.OriginalPrice,
                    Brand = product.Brand,
                    StockQuantity = product.StockQuantity,
                    CategoryId = product.CategoryId,
                    CategoryName = product.Category?.Name ?? "",
                    ImageUrl = product.ImageUrl,
                    Specifications = ParseSpecifications(product.Specifications),
                    Rating = product.Rating,
                    ReviewCount = product.ReviewCount,
                    IsNew = product.IsNew,
                    IsBestSeller = product.IsBestSeller,
                    CreatedAt = product.CreatedAt,
                    UpdatedAt = product.UpdatedAt
                };

                return Ok(ApiResponse<ProductResponseDto>.SuccessResult(productDto));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<ProductResponseDto>.ErrorResult(
                    "Đã xảy ra lỗi khi lấy thông tin sản phẩm", new List<string> { ex.Message }));
            }
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<ProductResponseDto>>> CreateProduct([FromForm] ProductCreateDto productDto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .ToList();
                    return BadRequest(ApiResponse<ProductResponseDto>.ErrorResult("Dữ liệu không hợp lệ", errors));
                }

                // Validate category exists
                var categoryExists = await _context.Categorys.AnyAsync(c => c.CategoryId == productDto.CategoryId);
                if (!categoryExists)
                {
                    return BadRequest(ApiResponse<ProductResponseDto>.ErrorResult("Danh mục không tồn tại"));
                }

                // Handle file upload
                string? imageFileName = null;
                if (productDto.File != null)
                {
                    imageFileName = await SaveFileAsync(productDto.File);
                }

                var product = new Product
                {
                    Name = productDto.Name,
                    Description = productDto.Description,
                    Price = productDto.Price,
                    OriginalPrice = productDto.OriginalPrice,
                    Brand = productDto.Brand,
                    StockQuantity = productDto.StockQuantity,
                    CategoryId = productDto.CategoryId,
                    ImageUrl = imageFileName ?? productDto.ImageUrl,
                    Specifications = productDto.Specifications ?? "{}",
                    IsNew = productDto.IsNew,
                    IsBestSeller = productDto.IsBestSeller,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.Products.Add(product);
                await _context.SaveChangesAsync();

                // Get the created product with category info
                var createdProduct = await _context.Products
                    .Include(p => p.Category)
                    .FirstOrDefaultAsync(p => p.ProductId == product.ProductId);

                if (createdProduct == null)
                {
                    return StatusCode(500, ApiResponse<ProductResponseDto>.ErrorResult("Lỗi khi tạo sản phẩm"));
                }

                var responseDto = new ProductResponseDto
                {
                    ProductId = createdProduct.ProductId,
                    Name = createdProduct.Name,
                    Description = createdProduct.Description,
                    Price = createdProduct.Price,
                    OriginalPrice = createdProduct.OriginalPrice,
                    Brand = createdProduct.Brand,
                    StockQuantity = createdProduct.StockQuantity,
                    CategoryId = createdProduct.CategoryId,
                    CategoryName = createdProduct.Category?.Name ?? "",
                    ImageUrl = createdProduct.ImageUrl,
                    Specifications = ParseSpecifications(createdProduct.Specifications),
                    Rating = createdProduct.Rating,
                    ReviewCount = createdProduct.ReviewCount,
                    IsNew = createdProduct.IsNew,
                    IsBestSeller = createdProduct.IsBestSeller,
                    CreatedAt = createdProduct.CreatedAt,
                    UpdatedAt = createdProduct.UpdatedAt
                };

                return CreatedAtAction(nameof(GetProduct), new { id = product.ProductId }, 
                    ApiResponse<ProductResponseDto>.SuccessResult(responseDto, "Tạo sản phẩm thành công"));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<ProductResponseDto>.ErrorResult(
                    "Đã xảy ra lỗi khi tạo sản phẩm", new List<string> { ex.Message }));
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<ProductResponseDto>>> UpdateProduct(int id, [FromForm] ProductUpdateDto productDto)
        {
            try
            {
                if (id != productDto.ProductId)
                {
                    return BadRequest(ApiResponse<ProductResponseDto>.ErrorResult("ID sản phẩm không khớp"));
                }

                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .ToList();
                    return BadRequest(ApiResponse<ProductResponseDto>.ErrorResult("Dữ liệu không hợp lệ", errors));
                }

                var existingProduct = await _context.Products.FindAsync(id);
                if (existingProduct == null)
                {
                    return NotFound(ApiResponse<ProductResponseDto>.ErrorResult("Không tìm thấy sản phẩm"));
                }

                // Validate category exists
                var categoryExists = await _context.Categorys.AnyAsync(c => c.CategoryId == productDto.CategoryId);
                if (!categoryExists)
                {
                    return BadRequest(ApiResponse<ProductResponseDto>.ErrorResult("Danh mục không tồn tại"));
                }

                // Handle file upload
                string? imageFileName = null;
                if (productDto.File != null)
                {
                    imageFileName = await SaveFileAsync(productDto.File);
                }

                // Update product
                existingProduct.Name = productDto.Name;
                existingProduct.Description = productDto.Description;
                existingProduct.Price = productDto.Price;
                existingProduct.OriginalPrice = productDto.OriginalPrice;
                existingProduct.Brand = productDto.Brand;
                existingProduct.StockQuantity = productDto.StockQuantity;
                existingProduct.CategoryId = productDto.CategoryId;
                existingProduct.ImageUrl = imageFileName ?? productDto.ImageUrl ?? existingProduct.ImageUrl;
                existingProduct.Specifications = productDto.Specifications ?? "{}";
                existingProduct.IsNew = productDto.IsNew;
                existingProduct.IsBestSeller = productDto.IsBestSeller;
                existingProduct.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                // Get updated product with category info
                var updatedProduct = await _context.Products
                    .Include(p => p.Category)
                    .FirstOrDefaultAsync(p => p.ProductId == id);

                if (updatedProduct == null)
                {
                    return StatusCode(500, ApiResponse<ProductResponseDto>.ErrorResult("Lỗi khi cập nhật sản phẩm"));
                }

                var responseDto = new ProductResponseDto
                {
                    ProductId = updatedProduct.ProductId,
                    Name = updatedProduct.Name,
                    Description = updatedProduct.Description,
                    Price = updatedProduct.Price,
                    OriginalPrice = updatedProduct.OriginalPrice,
                    Brand = updatedProduct.Brand,
                    StockQuantity = updatedProduct.StockQuantity,
                    CategoryId = updatedProduct.CategoryId,
                    CategoryName = updatedProduct.Category?.Name ?? "",
                    ImageUrl = updatedProduct.ImageUrl,
                    Specifications = ParseSpecifications(updatedProduct.Specifications),
                    Rating = updatedProduct.Rating,
                    ReviewCount = updatedProduct.ReviewCount,
                    IsNew = updatedProduct.IsNew,
                    IsBestSeller = updatedProduct.IsBestSeller,
                    CreatedAt = updatedProduct.CreatedAt,
                    UpdatedAt = updatedProduct.UpdatedAt
                };

                return Ok(ApiResponse<ProductResponseDto>.SuccessResult(responseDto, "Cập nhật sản phẩm thành công"));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<ProductResponseDto>.ErrorResult(
                    "Đã xảy ra lỗi khi cập nhật sản phẩm", new List<string> { ex.Message }));
            }
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult<ApiResponse<object?>>> DeleteProduct(int id)
        {
            try
            {
                var product = await _context.Products.FindAsync(id);
                if (product == null)
                {
                    return NotFound(ApiResponse<object?>.ErrorResult("Không tìm thấy sản phẩm"));
                }

                _context.Products.Remove(product);
                await _context.SaveChangesAsync();

                return Ok(ApiResponse<object?>.SuccessResult(null, "Xóa sản phẩm thành công"));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<object?>.ErrorResult(
                    "Đã xảy ra lỗi khi xóa sản phẩm", new List<string> { ex.Message }));
            }
        }

        // Additional endpoints
        [HttpGet("categories")]
        public async Task<ActionResult<ApiResponse<List<Category>>>> GetCategories()
        {
            try
            {
                var categories = await _context.Categorys.ToListAsync();
                return Ok(ApiResponse<List<Category>>.SuccessResult(categories));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<List<Category>>.ErrorResult(
                    "Đã xảy ra lỗi khi lấy danh sách danh mục", new List<string> { ex.Message }));
            }
        }

        [HttpGet("brands")]
        public async Task<ActionResult<ApiResponse<List<string>>>> GetBrands()
        {
            try
            {
                var brands = await _context.Products
                    .Where(p => !string.IsNullOrEmpty(p.Brand))
                    .Select(p => p.Brand)
                    .Distinct()
                    .OrderBy(b => b)
                    .ToListAsync();

                return Ok(ApiResponse<List<string>>.SuccessResult(brands));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<List<string>>.ErrorResult(
                    "Đã xảy ra lỗi khi lấy danh sách thương hiệu", new List<string> { ex.Message }));
            }
        }

        [HttpGet("category/{categoryId}")]
        public async Task<ActionResult<ApiResponse<PagedResult<ProductResponseDto>>>> GetProductsByCategory(
            int categoryId, [FromQuery] ProductQueryDto query)
        {
            query.CategoryId = categoryId;
            return await GetProducts(query);
        }

        [HttpGet("search")]
        public async Task<ActionResult<ApiResponse<PagedResult<ProductResponseDto>>>> SearchProducts(
            [FromQuery] string searchTerm, [FromQuery] ProductQueryDto query)
        {
            query.Search = searchTerm;
            return await GetProducts(query);
        }

        [HttpGet("image/{fileName}")]
        public async Task<IActionResult> GetImage(string fileName)
        {
            try
            {
                var imagePath = Path.Combine(@"D:\ImageStorage", fileName);
                
                if (!System.IO.File.Exists(imagePath))
                {
                    return NotFound("Không tìm thấy ảnh");
                }

                var fileInfo = new FileInfo(imagePath);
                var fileExtension = Path.GetExtension(fileName).ToLowerInvariant();
                var contentType = fileExtension switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".gif" => "image/gif",
                    ".bmp" => "image/bmp",
                    ".webp" => "image/webp",
                    _ => "application/octet-stream"
                };

                // Thêm cache headers để tối ưu hiệu suất
                Response.Headers["Cache-Control"] = "public, max-age=31536000"; // Cache 1 năm
                Response.Headers["Expires"] = DateTime.UtcNow.AddYears(1).ToString("R");
                Response.Headers["Last-Modified"] = fileInfo.LastWriteTimeUtc.ToString("R");

                // Kiểm tra If-Modified-Since header
                if (Request.Headers.ContainsKey("If-Modified-Since"))
                {
                    if (DateTime.TryParse(Request.Headers["If-Modified-Since"], out var ifModifiedSince))
                    {
                        if (fileInfo.LastWriteTimeUtc <= ifModifiedSince.AddSeconds(1))
                        {
                            return StatusCode(304); // Not Modified
                        }
                    }
                }

                var fileBytes = await System.IO.File.ReadAllBytesAsync(imagePath);
                return File(fileBytes, contentType);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Lỗi khi tải ảnh: {ex.Message}");
            }
        }

        // Helper method to parse JSON specifications
        private object? ParseSpecifications(string? specifications)
        {
            if (string.IsNullOrEmpty(specifications))
                return null;

            try
            {
                return JsonSerializer.Deserialize<object>(specifications);
            }
            catch
            {
                return specifications; // Return as string if JSON parsing fails
            }
        }

        // Helper for export: return parsed JSON or raw string
        private object? TryParseJson(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            try
            {
                return JsonSerializer.Deserialize<object>(s);
            }
            catch
            {
                return s;
            }
        }

        private async Task<string> SaveFileAsync(IFormFile file)
        {
            try
            {
                var uploadPath = @"D:\ImageStorage";
                if (!Directory.Exists(uploadPath))
                {
                    Directory.CreateDirectory(uploadPath);
                }

                var fileExtension = Path.GetExtension(file.FileName);
                var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
                var filePath = Path.Combine(uploadPath, uniqueFileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                return uniqueFileName;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error saving file: {ex.Message}", ex);
            }
        }
    }
}
