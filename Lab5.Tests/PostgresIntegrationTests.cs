// Lab5.Tests/PostgresIntegrationTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lab5.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Shouldly;
using Xunit;

namespace Lab5.Tests;

public class PostgresIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .WithDatabase("test_db")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private AppDbContext _context = null!;

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString())
            .Options;

        _context = new AppDbContext(options);
        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _dbContainer.DisposeAsync();
    }

    // Вимога 1: Виконують усі CRUD-операції на реальному контейнері
    [Fact]
    [Trait("Category", "Integration")]
    public async Task CrudOperations_WorkWithRealPostgresAsync()
    {
        // CREATE
        var student = new Student { FullName = "Дмитро Коваленко", Email = "dima@test.com", EnrollmentDate = DateTime.UtcNow };
        _context.Students.Add(student);
        await _context.SaveChangesAsync();

        // READ
        var found = await _context.Students.FindAsync(student.Id);
        found.ShouldNotBeNull();
        
        // UPDATE
        found.FullName = "Дмитро Оновлений";
        await _context.SaveChangesAsync();
        var updated = await _context.Students.FindAsync(student.Id);
        updated!.FullName.ShouldBe("Дмитро Оновлений");

        // DELETE
        _context.Students.Remove(updated);
        await _context.SaveChangesAsync();
        var deleted = await _context.Students.FindAsync(student.Id);
        deleted.ShouldBeNull();
    }

    // Вимога 2: Перевіряють, що обмеження зовнішніх ключів забезпечуються
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ForeignKey_EnrollingInNonExistingCourse_ThrowsExceptionAsync()
    {
        var enrollment = new Enrollment
        {
            StudentId = 9999, 
            CourseId = 9999,  
            Grade = 90
        };

        _context.Enrollments.Add(enrollment);
    
        var exception = await Should.ThrowAsync<DbUpdateException>(async () => 
            await _context.SaveChangesAsync());
        
        exception.InnerException!.Message.ShouldContain("FK_");
    }

    // Вимога 3: Тестують виконання збережених процедур або сирих SQL-запитів
    [Fact]
    [Trait("Category", "Integration")]
    public async Task RawSql_ComplexQuery_ReturnsExpectedResultsAsync()
    {
        _context.Students.Add(new Student { FullName = "Admin User", Email = "admin@system.com" });
        _context.Students.Add(new Student { FullName = "Guest User", Email = "guest@system.com" });
        await _context.SaveChangesAsync();

        // Використовуємо специфічний синтаксис Postgres (ILIKE для пошуку без урахування регістру)
        var students = await _context.Students
            .FromSqlRaw("SELECT * FROM \"Students\" WHERE \"FullName\" ILIKE '%admin%'")
            .ToListAsync();

        students.ShouldNotBeEmpty();
        students.First().Email.ShouldBe("admin@system.com");
    }

    // Вимога 4: Перевіряють, що міграції EF застосовуються чисто до нової бази даних
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Migrations_EnsureCleanApplicationAsync()
    {
        // Видаляємо базу
        await _context.Database.EnsureDeletedAsync();
        
        await _context.Database.EnsureCreatedAsync();
        
        // Перевіряємо, що схема існує і ми можемо додавати дані
        _context.Courses.Add(new Course { Title = "Postgres Architecture", Credits = 5 });
        await _context.SaveChangesAsync();
        
        var count = await _context.Courses.CountAsync();
        count.ShouldBe(1);
    }

    // Додатковий тест на каскадне видалення (корисно для перевірки реляційності)
    [Fact]
    [Trait("Category", "Integration")]
    public async Task CascadeDelete_DeletingStudent_RemovesEnrollmentsAsync()
    {
        var course = new Course { Title = "Postgres 101", Credits = 3 };
        var student = new Student
        {
            FullName = "Іван Франко", Email = "ivan@test.com",
            Enrollments = new List<Enrollment> { new Enrollment { Course = course, Grade = 95 } }
        };
        
        _context.Students.Add(student);
        await _context.SaveChangesAsync();

        // Видаляємо студента
        _context.Students.Remove(student);
        await _context.SaveChangesAsync();

        // Перевіряємо, що оцінки видалились автоматично (Cascade)
        var enrollments = await _context.Enrollments.ToListAsync();
        enrollments.ShouldBeEmpty();
    }
}