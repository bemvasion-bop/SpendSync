using LedgerFlow.API.Data;
using LedgerFlow.API.Models;
using LedgerFlow.API.Middleware;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using LedgerFlow.API.Services;
using System.Text;



var builder = WebApplication.CreateBuilder(args);



// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi

builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<ExpenseService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<DepartmentService>();
builder.Services.AddScoped<PlanEnforcementService>();
builder.Services.AddScoped<CompanyService>();
builder.Services.AddScoped<ApprovalService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<ISuperAdminService, SuperAdminService>();
builder.Services.AddScoped<OtpService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<ReportsService>();
builder.Services.AddScoped<UserSettingsService>();
builder.Services.AddScoped<SubscriptionService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<DatabaseSeeder>(); // Demo data seeding service
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var key = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]);

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key)
        };
    });


builder.Services.AddAuthorization(options=>
{
        options.AddPolicy("AdminOnly", policy=>
                policy.RequireRole("Admin"));

        options.AddPolicy("EmployeeOnly", policy=>
                policy.RequireRole("Employee"));

        options.AddPolicy("VerifiedUser", policy=>
                policy.RequireClaim("IsVerified", "True"));


});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy =>
        {
            policy
                .WithOrigins(
                    "http://localhost:3000",
                    "http://localhost:3001",
                    "https://localhost:3000",
                    "https://localhost:3001",
                    "http://localhost:5173",  // Vite default port (if needed)
                    "https://localhost:5173",
                    "http://172.21.12.28:3001",  // Network IP address
                    "https://172.21.12.28:3001",
                    "http://localhost:5256",  // Backend URL
                    "https://localhost:5256"
                )
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
                .WithExposedHeaders("*"); // Expose all headers to frontend
        });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    
}

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        var exceptionHandlerFeature = context.Features.Get<IExceptionHandlerFeature>();
        var errorMessage = exceptionHandlerFeature?.Error?.Message ?? "Something went wrong";

        await context.Response.WriteAsJsonAsync(new { message = errorMessage });
    });
});

// IMPORTANT: CORS must be before Authentication and Authorization
app.UseCors("AllowFrontend");

app.UseHttpsRedirection();

// Configure static files for uploads folder
app.UseStaticFiles(); // Serve from wwwroot (default)

// Ensure uploads folder exists
var uploadsPath = Path.Combine(builder.Environment.ContentRootPath, "uploads");

if (!Directory.Exists(uploadsPath))
{
    Directory.CreateDirectory(uploadsPath);
}

// Serve uploaded receipts from uploads folder
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});

app.UseAuthentication();
app.UseSubscriptionEnforcement(); // Add subscription middleware
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<AppDbContext>();
    var logger = services.GetRequiredService<ILogger<Program>>();

    try
    {
        // ========================================
        // CRITICAL: Log active database connection
        // ========================================
        var connectionString = context.Database.GetDbConnection().ConnectionString;
        var databaseName = context.Database.GetDbConnection().Database;
        
        Console.WriteLine("========================================");
        Console.WriteLine("DATABASE CONNECTION VERIFICATION");
        Console.WriteLine("========================================");
        Console.WriteLine($"Connection String: {connectionString}");
        Console.WriteLine($"Active Database: {databaseName}");
        Console.WriteLine("========================================");
        
        logger.LogInformation("========================================");
        logger.LogInformation("DATABASE CONNECTION VERIFICATION");
        logger.LogInformation($"Active Database: {databaseName}");
        logger.LogInformation("========================================");

        // Apply migrations automatically
        logger.LogInformation("Applying database migrations...");
        context.Database.Migrate();
        logger.LogInformation("Database migrations applied successfully.");

        // ========================================
        // CRITICAL: Verify Plans table access
        // ========================================
        try
        {
            var plansCount = context.Plans.Count();
            Console.WriteLine($"✅ Plans table accessible: {plansCount} plans found");
            logger.LogInformation($"✅ Plans table accessible: {plansCount} plans found");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ ERROR accessing Plans table: {ex.Message}");
            logger.LogError(ex, "❌ ERROR accessing Plans table");
            throw;
        }

        // Seed plans - SaaS Subscription Structure (Quarterly & Yearly billing)
        if (!context.Plans.Any())
        {
            logger.LogInformation("Seeding SaaS Subscription Plans...");
            context.Plans.AddRange(
                new Plan
                {
                    Name = "Starter",
                    Description = "Perfect for startups and small teams getting started",
                    QuarterlyPrice = 1499.00m,
                    YearlyPrice = 5499.00m,
                    MaxUsers = 10,
                    MaxExpensesPerMonth = 100,
                    CanUploadReceipt = true,  // ✅ CHANGED: Receipt uploads now available in Starter
                    HasAdvancedReports = false,
                    HasAdvancedAnalytics = false,
                    HasDepartmentAnalytics = false,  // ✅ CHANGED: Departments only in Business
                    HasRoleBasedWorkflows = false,  // ✅ CHANGED: Renamed from HasMultiLevelApprovals
                    HasPrioritySupport = false,
                    TrialDays = 14
                },
                new Plan
                {
                    Name = "Business",
                    Description = "Built for growing companies with advanced workflows",
                    QuarterlyPrice = 6999.00m,
                    YearlyPrice = 24999.00m,
                    MaxUsers = -1, // Unlimited
                    MaxExpensesPerMonth = -1, // Unlimited
                    CanUploadReceipt = true,
                    HasAdvancedReports = true,
                    HasAdvancedAnalytics = true,
                    HasDepartmentAnalytics = true,
                    HasRoleBasedWorkflows = true,  // ✅ CHANGED: Renamed from HasMultiLevelApprovals
                    HasPrioritySupport = true,
                    TrialDays = 14
                }
            );
            context.SaveChanges();
            logger.LogInformation("SaaS Subscription Plans seeded successfully (Starter, Business).");
        }

        // Seed roles
        if (!context.Roles.Any())
        {
            logger.LogInformation("Seeding Roles...");
            context.Roles.AddRange(
                new Role { RoleName = "SuperAdmin" },
                new Role { RoleName = "Admin" },
                new Role { RoleName = "Employee" },
                new Role { RoleName = "Finance" },
                new Role { RoleName = "Audit" }
            );
            context.SaveChanges();
            logger.LogInformation("Roles seeded successfully.");
        }
        else
        {
            // Check if SuperAdmin role exists, if not add it
            if (!context.Roles.Any(r => r.RoleName == "SuperAdmin"))
            {
                logger.LogInformation("Adding missing SuperAdmin role...");
                context.Roles.Add(new Role { RoleName = "SuperAdmin" });
                context.SaveChanges();
                logger.LogInformation("SuperAdmin role added successfully.");
            }
        }

        // Seed categories
        if (!context.Categories.Any())
        {
            logger.LogInformation("Seeding Categories...");
            context.Categories.AddRange(
                new LedgerFlow.API.Models.Category { Name = "Travel",          Description = "Transportation, flights, hotels" },
                new LedgerFlow.API.Models.Category { Name = "Meals",           Description = "Food and beverages" },
                new LedgerFlow.API.Models.Category { Name = "Office Supplies", Description = "Stationery, equipment, consumables" },
                new LedgerFlow.API.Models.Category { Name = "Software",        Description = "Licenses, subscriptions, tools" },
                new LedgerFlow.API.Models.Category { Name = "Utilities",       Description = "Internet, phone, electricity" },
                new LedgerFlow.API.Models.Category { Name = "Other",           Description = "Miscellaneous expenses" }
            );
            context.SaveChanges();
            logger.LogInformation("Categories seeded successfully.");
        }

        // Seed a demo company if none exists
        if (!context.Companies.Any())
        {
            logger.LogInformation("Seeding Demo Company...");
            var businessPlan = context.Plans.First(p => p.Name == "Business");
            context.Companies.Add(new Company
            {
                Name = "Demo Company",
                Email = "demo@company.com",
                PlanId = businessPlan.Id,
                SubscriptionStatus = "Trial",
                TrialEndsAt = DateTime.UtcNow.AddDays(14),
                Status = "Active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            context.SaveChanges();
            logger.LogInformation("Demo Company seeded successfully with Business plan.");
        }

        // Seed default departments for Demo Company
        var demoCompany = context.Companies.FirstOrDefault(c => c.Name == "Demo Company");
        if (demoCompany != null && !context.Departments.Any(d => d.CompanyId == demoCompany.Id))
        {
            logger.LogInformation("Seeding Default Departments for Demo Company...");
            context.Departments.AddRange(
                new Department { Name = "Finance",     Description = "Financial operations and accounting", CompanyId = demoCompany.Id, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new Department { Name = "HR",          Description = "Human resources and recruitment",     CompanyId = demoCompany.Id, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new Department { Name = "Operations",  Description = "Day-to-day business operations",      CompanyId = demoCompany.Id, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new Department { Name = "IT",          Description = "Information technology and systems",  CompanyId = demoCompany.Id, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new Department { Name = "Audit",       Description = "Internal audit and compliance",       CompanyId = demoCompany.Id, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new Department { Name = "Management",  Description = "Executive management and leadership", CompanyId = demoCompany.Id, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
            );
            context.SaveChanges();
            logger.LogInformation("Default Departments seeded successfully.");
        }

        // Seed SuperAdmin user (platform administrator)
        var superAdminEmail = "superadmin@spendsync.com";
        var existingSuperAdmin = context.Users.FirstOrDefault(u => u.Email == superAdminEmail);
        
        if (existingSuperAdmin == null)
        {
            logger.LogInformation("=== SEEDING SUPERADMIN USER ===");
            
            // Ensure SuperAdmin role exists
            var superAdminRole = context.Roles.FirstOrDefault(r => r.RoleName == "SuperAdmin");
            if (superAdminRole == null)
            {
                logger.LogWarning("SuperAdmin role not found. Creating it now...");
                superAdminRole = new Role { RoleName = "SuperAdmin" };
                context.Roles.Add(superAdminRole);
                context.SaveChanges();
                logger.LogInformation($"SuperAdmin role created with ID: {superAdminRole.Id}");
            }
            else
            {
                logger.LogInformation($"SuperAdmin role found with ID: {superAdminRole.Id}");
            }

            // Get or create platform company for SuperAdmin
            var platformCompany = context.Companies.FirstOrDefault(c => c.Name == "SpendSync Platform");
            if (platformCompany == null)
            {
                logger.LogInformation("Creating SpendSync Platform company...");
                var starterPlan = context.Plans.FirstOrDefault(p => p.Name == "Starter");
                if (starterPlan == null)
                {
                    logger.LogError("Starter plan not found. Cannot create platform company.");
                    throw new Exception("Starter plan not found");
                }

                platformCompany = new Company
                {
                    Name = "SpendSync Platform",
                    Email = "platform@spendsync.com",
                    PlanId = starterPlan.Id,
                    SubscriptionStatus = "Active",
                    Status = "Active",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                context.Companies.Add(platformCompany);
                context.SaveChanges();
                logger.LogInformation($"SpendSync Platform company created with ID: {platformCompany.Id}");
            }
            else
            {
                logger.LogInformation($"SpendSync Platform company found with ID: {platformCompany.Id}");
            }

            // Create SuperAdmin user
            logger.LogInformation("Creating SuperAdmin user...");
            var superAdmin = new User
            {
                FirstName = "Super",
                LastName = "Admin",
                Email = superAdminEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("SuperAdmin123!"),
                RoleId = superAdminRole.Id,
                CompanyId = platformCompany.Id,
                IsVerified = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            context.Users.Add(superAdmin);
            context.SaveChanges();
            
            logger.LogInformation("=== SUPERADMIN USER CREATED SUCCESSFULLY ===");
            logger.LogInformation($"SuperAdmin User ID: {superAdmin.Id}");
            logger.LogInformation($"SuperAdmin Role ID: {superAdminRole.Id}");
            logger.LogInformation($"Platform Company ID: {platformCompany.Id}");
            logger.LogInformation($"SuperAdmin Email: {superAdminEmail}");
            logger.LogInformation($"SuperAdmin Password: SuperAdmin123!");
            logger.LogInformation("===========================================");
        }
        else
        {
            logger.LogInformation($"SuperAdmin user already exists with ID: {existingSuperAdmin.Id}");
        }

        logger.LogInformation("Database initialization completed successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while initializing the database.");
        throw;
    }
}

app.Run();


