using System;
using System.Net.Http;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Testcontainers.PostgreSql;
using Lab5.Data;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using System.Net.Http.Json;
using Xunit;

namespace Lab5.Tests;

public class EndpointE2ETests : IAsyncLifetime
{
    private INetwork _network = null!;
    private PostgreSqlContainer _dbContainer = null!;
    private IContainer _apiContainer = null!;
    private HttpClient _httpClient = null!;

    public async Task InitializeAsync()
    {
        // 1. СТВОРЮЄМО СПІЛЬНУ МЕРЕЖУ (щоб контейнери бачили одне одного)
        _network = new NetworkBuilder()
            .WithName(Guid.NewGuid().ToString("D"))
            .Build();
        await _network.CreateAsync();

        // 2. ЗАПУСКАЄМО БАЗУ ДАНИХ (в цій мережі під ім'ям "test-db")
        _dbContainer = new PostgreSqlBuilder()
            .WithImage("postgres:15-alpine")
            .WithNetwork(_network)
            .WithNetworkAliases("test-db") // Це ім'я будемо використовувати в API
            .WithDatabase("test_db")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _dbContainer.StartAsync();

        // 3. СТВОРЮЄМО ТАБЛИЦІ В БАЗІ ДАНИХ
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString())
            .Options;
            
        using (var context = new AppDbContext(options))
        {
            
            await context.Database.EnsureCreatedAsync();
        }

        // 4. ЗАПУСКАЄМО API
        var internalConnString = _dbContainer.GetConnectionString();

        _apiContainer = new ContainerBuilder()
            .WithImage("lectures-api:test")
            .WithNetwork(_network)
            .WithPortBinding(8080, true) 
            .WithEnvironment("ConnectionStrings__DefaultConnection", internalConnString)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8080))
            .Build();

        await _apiContainer.StartAsync();

        // 5. НАЛАШТОВУЄМО HTTP КЛІЄНТ
        var mappedPort = _apiContainer.GetMappedPublicPort(8080);
        _httpClient = new HttpClient 
        { 
            BaseAddress = new Uri($"http://{_apiContainer.Hostname}:{mappedPort}") 
        };
    }

    public async Task DisposeAsync()
    {
        _httpClient?.Dispose();
        if (_apiContainer != null) await _apiContainer.DisposeAsync();
        if (_dbContainer != null) await _dbContainer.DisposeAsync();
        if (_network != null) await _network.DeleteAsync();
    }

    // --- ТЕСТИ ---

    [Fact]
    [Trait("Category", "E2E")]
    public async Task GetStudentsEndpoint_WhenEmpty_ReturnsEmptyArray()
    {
        var response = await _httpClient.GetAsync("/api/students");
        
        response.IsSuccessStatusCode.ShouldBeTrue();

        var content = await response.Content.ReadAsStringAsync();
        content.ShouldBe("[]"); 
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task PostStudent_ValidData_ReturnsCreated()
    {
        var newStudentJson = new StringContent(
            "{\"fullName\": \"Тест Тестенко\", \"email\": \"test@endpoint.com\"}", 
            System.Text.Encoding.UTF8, 
            "application/json");

        var postResponse = await _httpClient.PostAsync("/api/students", newStudentJson);

        postResponse.EnsureSuccessStatusCode();

        var getResponse = await _httpClient.GetAsync("/api/students");
        var getContent = await getResponse.Content.ReadAsStringAsync();
        getContent.ShouldContain("test@endpoint.com");
    }
    
    [Fact]
    [Trait("Category", "E2E")]
    public async Task GetStudentById_ExistingStudent_ReturnsStudentAsync()
    {
        // Arrange: Спочатку створюємо студента
        var newStudent = new StringContent(
            "{\"fullName\": \"Іван Тестовий\", \"email\": \"ivan@id.com\"}", 
            System.Text.Encoding.UTF8, "application/json");
        var postResponse = await _httpClient.PostAsync("/api/students", newStudent);
        
        // Дістаємо ID створеного студента з відповіді
        var createdStudent = await postResponse.Content.ReadFromJsonAsync<StudentResponse>();
        var studentId = createdStudent!.Id;

        // Act: Намагаємось отримати його за ID
        var getResponse = await _httpClient.GetAsync($"/api/students/{studentId}");

        // Assert
        getResponse.IsSuccessStatusCode.ShouldBeTrue();
        var content = await getResponse.Content.ReadFromJsonAsync<StudentResponse>();
        content!.Email.ShouldBe("ivan@id.com");
    }

    // Тест 4: Видалення студента
    [Fact]
    [Trait("Category", "E2E")]
    public async Task DeleteStudent_ExistingStudent_RemovesFromDbAsync()
    {
        // Arrange: Створюємо студента для видалення
        var newStudent = new StringContent(
            "{\"fullName\": \"На видалення\", \"email\": \"delete@me.com\"}", 
            System.Text.Encoding.UTF8, "application/json");
        var postResponse = await _httpClient.PostAsync("/api/students", newStudent);
        var createdStudent = await postResponse.Content.ReadFromJsonAsync<StudentResponse>();
        var studentId = createdStudent!.Id;

        // Act: Видаляємо його
        var deleteResponse = await _httpClient.DeleteAsync($"/api/students/{studentId}");
        
        // Assert: Має повернути 204 No Content
        deleteResponse.StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);

        // Перевіряємо, що його більше немає (GET має повернути 404)
        var getResponse = await _httpClient.GetAsync($"/api/students/{studentId}");
        getResponse.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }

    // Допоміжна моделька для читання JSON у тестах
    public record StudentResponse(int Id, string FullName, string Email);
}