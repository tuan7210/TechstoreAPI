using Microsoft.EntityFrameworkCore;
using TechstoreBackend.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using TechstoreBackend.Middleware;
using TechstoreBackend.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ✅ Add Exception Handler
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// ✅ Add DbContext with MySQL
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("DefaultConnection"))
    )
);

// ✅ JWT Authentication
var jwtSettings = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? ""))
    };
});

// PAYOS - TODO: Configure PayOS when SDK is properly set up
// var payOSConfig = builder.Configuration.GetSection("PayOS");
// global::PayOS.PayOSClient payOSInstance = new global::PayOS.PayOSClient(
//     payOSConfig["ClientId"] ?? throw new Exception("Missing ClientId"),
//     payOSConfig["ApiKey"] ?? throw new Exception("Missing ApiKey"),
//     payOSConfig["ChecksumKey"] ?? throw new Exception("Missing ChecksumKey")
// );
// builder.Services.AddSingleton(payOSInstance);

// ✅ Add HttpClient for PayOS
builder.Services.AddHttpClient();

// ✅ Add PayOS Service
builder.Services.AddScoped<PayOSService>();

// ✅ CORS setup
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173") // React frontend
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// ✅ Add Exception Handler
app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// ✅ Thứ tự quan trọng: CORS → Auth → Controller
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();
