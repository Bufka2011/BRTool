using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    private static readonly string LogFilePath = "bugreport.log"; // Путь к файлу логов программы
    private static readonly string MemoryFilePath = "memory.txt"; // Путь к файлу для запоминания ника
    private static readonly string DxDiagTempFile = "dxdiag_output.txt"; // Временный файл для DxDiag

    static async Task Main(string[] args)
    {
        try
        {
            LogToFile("Программа запущена.");

            Console.WriteLine("Сбор информации о ПК, это может занять несколько секунд...");
            LogToFile("Сбор информации о ПК...");
            string pcInfo = GetPcInfo();

            // Выбираем версию игры
            string gameVersion = SelectGameVersion();
            string versionFolder = gameVersion == "1.12.2" ? "1122" : "1710";
            LogToFile($"Выбрана версия игры: {gameVersion}");

            // Путь к папке с логами и краш-репортами
            string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "nuclearcraft", "clients", versionFolder);
            string logDir = Path.Combine(baseDir, "logs");
            string crashDir = Path.Combine(baseDir, "crash-reports");

            // Загружаем логи и краш-репорты
            Console.WriteLine("Загрузка логов на mclo.gs...");
            LogToFile("Загрузка логов на mclo.gs...");

            string latestLogPath = Path.Combine(logDir, "latest.log");
            string fmlLogPath = Path.Combine(logDir, "fml-client-latest.log");
            string crashReportPath = GetLatestCrashReport(crashDir);

            string latestLogUrl = await UploadLogAsync(latestLogPath, "latest.log");
            Console.WriteLine($"Ссылка на latest.log: {latestLogUrl}");
            LogToFile($"Ссылка на latest.log: {latestLogUrl}");

            string fmlLogUrl = await UploadLogAsync(fmlLogPath, "fml-client-latest.log");
            Console.WriteLine($"Ссылка на fml-client-latest.log: {fmlLogUrl}");
            LogToFile($"Ссылка на fml-client-latest.log: {fmlLogUrl}");

            string crashReportUrl = crashReportPath != null
                ? await UploadLogAsync(crashReportPath, Path.GetFileName(crashReportPath))
                : "Краш-репорт не найден.";
            if (crashReportPath != null)
            {
                Console.WriteLine($"Ссылка на краш-репорт: {crashReportUrl}");
                LogToFile($"Ссылка на краш-репорт: {crashReportUrl}");
            }
            else
            {
                LogToFile("Краш-репорт не найден.");
            }

            // Получаем ник (с запоминанием)
            string nickname = GetNickname();
            string message = GetUserInput("Пожалуйста, опишите проблему максимально подробно. Расскажите, что вы делали и что произошло. > ");

            // Формируем эмбед для Discord
            var embed = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = nickname,
                        description = message,
                        color = 0xFF0000, // Красный цвет
                        fields = new[]
                        {
                            new { name = "Версия игры", value = gameVersion, inline = false },
                            new { name = "Характеристики ПК", value = pcInfo, inline = false },
                            new { name = "Ссылка на latest.log", value = latestLogUrl, inline = false },
                            new { name = "Ссылка на fml-client-latest.log", value = fmlLogUrl, inline = false },
                            new { name = "Ссылка на crash-report", value = crashReportUrl, inline = false }
                        }
                    }
                }
            };
            string json = JsonSerializer.Serialize(embed);

            // Отправляем в Discord
            string webhookUrl = "ВАШ_WEBHOOK_URL";
            if (string.IsNullOrEmpty(webhookUrl) || webhookUrl == "ВАШ_WEBHOOK_URL")
            {
                throw new ArgumentException("URL WebHook не указан. Пожалуйста, замените 'ВАШ_WEBHOOK_URL' на действительный URL.");
            }

            Console.WriteLine("Отправка данных в Discord...");
            LogToFile("Отправка данных в Discord...");
            await SendToDiscordAsync(webhookUrl, json);

            Console.WriteLine("Данные успешно отправлены!");
            LogToFile("Данные успешно отправлены!");
        }
        catch (Exception ex)
        {
            string errorMessage = $"Ошибка: {ex.Message}";
            Console.WriteLine(errorMessage);
            LogToFile(errorMessage);
        }
        finally
        {
            // Добавляем задержку в 5 секунд перед закрытием
            Console.WriteLine("Программа закроется через 5 секунд...");
            await Task.Delay(5000); // Задержка 5000 миллисекунд (5 секунд)
        }
    }

    // Метод для сбора информации о ПК
    // Метод для сбора информации о ПК
    static string GetPcInfo()
    {
        StringBuilder info = new StringBuilder();

        // Процессор
        try
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
            {
                foreach (var obj in searcher.Get())
                {
                    info.AppendLine($"Процессор: {obj["Name"]}");
                    info.AppendLine($"Скорость: {obj["CurrentClockSpeed"]} МГц");
                    info.AppendLine($"Ядра: {obj["NumberOfCores"]}");
                    info.AppendLine($"Потоки: {obj["NumberOfLogicalProcessors"]}");
                }
            }
        }
        catch (Exception ex)
        {
            string errorMessage = $"Ошибка при получении информации о процессоре: {ex.Message}";
            if (ex.InnerException != null)
            {
                errorMessage += $"\nВнутренняя ошибка: {ex.InnerException.Message}";
            }
            LogToFile(errorMessage);
            info.AppendLine("Процессор: Неизвестно (ошибка при получении данных)");
        }

        info.AppendLine(); // Пропуск

        // Видеокарта (используем DxDiag для точного объёма памяти)
        (string gpuName, string vram) = GetGpuInfoFromDxDiag();
        info.AppendLine($"Видеокарта: {gpuName}");
        info.AppendLine($"Память: {vram}");

        info.AppendLine(); // Пропуск

        // Оперативная память (скорость только один раз)
        try
        {
            long totalRam = 0;
            string ramSpeed = "Неизвестно";
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory"))
            {
                bool firstModule = true;
                foreach (var obj in searcher.Get())
                {
                    totalRam += Convert.ToInt64(obj["Capacity"]);
                    if (firstModule)
                    {
                        ramSpeed = obj["Speed"]?.ToString() ?? "Неизвестно";
                        firstModule = false;
                    }
                }
            }
            info.AppendLine($"Скорость RAM: {ramSpeed} МГц");
            info.AppendLine($"Общий объём RAM: {totalRam / 1024 / 1024 / 1024} ГБ");
        }
        catch (Exception ex)
        {
            string errorMessage = $"Ошибка при получении информации о RAM: {ex.Message}";
            if (ex.InnerException != null)
            {
                errorMessage += $"\nВнутренняя ошибка: {ex.InnerException.Message}";
            }
            LogToFile(errorMessage);
            info.AppendLine("Скорость RAM: Неизвестно (ошибка при получении данных)");
            info.AppendLine("Общий объём RAM: Неизвестно (ошибка при получении данных)");
        }

        info.AppendLine(); // Пропуск

        // Свободное место на диске C
        try
        {
            DriveInfo drive = new DriveInfo("C");
            info.AppendLine($"Свободно на диске C: {drive.AvailableFreeSpace / 1024 / 1024 / 1024} ГБ");
        }
        catch (Exception ex)
        {
            string errorMessage = $"Ошибка при получении информации о диске C: {ex.Message}";
            if (ex.InnerException != null)
            {
                errorMessage += $"\nВнутренняя ошибка: {ex.InnerException.Message}";
            }
            LogToFile(errorMessage);
            info.AppendLine("Свободно на диске C: Неизвестно (ошибка при получении данных)");
        }

        return info.ToString();
    }

    // Метод для получения информации о видеокарте через DxDiag
    static (string gpuName, string vram) GetGpuInfoFromDxDiag()
    {
        try
        {
            // Запускаем dxdiag и экспортируем отчёт в файл
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "dxdiag",
                Arguments = $"/t {DxDiagTempFile}",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(startInfo))
            {
                process.WaitForExit(); // Ждём завершения dxdiag
            }

            // Даём небольшую задержку, чтобы файл точно успел создаться
            System.Threading.Thread.Sleep(1000);

            if (!File.Exists(DxDiagTempFile))
            {
                return ("Неизвестно", "Неизвестно");
            }

            // Читаем отчёт
            string[] lines = File.ReadAllLines(DxDiagTempFile);
            string gpuName = "Неизвестно";
            string vram = "Неизвестно";
            int maxMemoryMb = 0;

            // Переменные для текущего устройства
            string currentGpuName = null;
            int currentMemoryMb = 0;

            foreach (string line in lines)
            {
                if (line.Contains("Card name:"))
                {
                    // Если уже нашли устройство, проверяем предыдущее
                    if (currentGpuName != null && !IsVirtualDevice(currentGpuName))
                    {
                        if (currentMemoryMb > maxMemoryMb)
                        {
                            maxMemoryMb = currentMemoryMb;
                            gpuName = currentGpuName;
                            vram = $"{Math.Ceiling(currentMemoryMb / 1024.0)} ГБ";
                        }
                    }
                    // Сбрасываем для нового устройства
                    currentGpuName = line.Split(':')[1].Trim();
                    currentMemoryMb = 0;
                }
                else if (line.Contains("Display Memory:"))
                {
                    string memoryStr = line.Split(':')[1].Trim(); // "16384 MB"
                    string memoryValue = memoryStr.Split(' ')[0]; // "16384"
                    if (int.TryParse(memoryValue, out int memoryMb))
                    {
                        currentMemoryMb = memoryMb;
                    }
                }
            }

            // Проверяем последнее устройство
            if (currentGpuName != null && !IsVirtualDevice(currentGpuName))
            {
                if (currentMemoryMb > maxMemoryMb)
                {
                    maxMemoryMb = currentMemoryMb;
                    gpuName = currentGpuName;
                    vram = $"{Math.Ceiling(currentMemoryMb / 1024.0)} ГБ";
                }
            }

            // Если ничего подходящего не нашли
            if (maxMemoryMb == 0)
            {
                gpuName = "Неизвестно";
                vram = "Неизвестно";
            }

            // Удаляем временный файл
            File.Delete(DxDiagTempFile);

            return (gpuName, vram);
        }
        catch (Exception ex)
        {
            LogToFile($"Ошибка при получении данных через DxDiag: {ex.Message}");
            return ("Неизвестно", "Неизвестно");
        }
    }

    // Метод для проверки, является ли устройство виртуальным
    static bool IsVirtualDevice(string gpuName)
    {
        string nameLower = gpuName.ToLower();
        return nameLower.Contains("virtual") || nameLower.Contains("monitor") || nameLower.Contains("display");
    }

    // Метод для выбора версии игры
    static string SelectGameVersion()
    {
        Console.WriteLine("Выберите версию игры:");
        Console.WriteLine("1. 1.12.2");
        Console.WriteLine("2. 1.7.10");

        int choice;
        while (true)
        {
            Console.Write("Введите номер (1 или 2): ");
            if (int.TryParse(Console.ReadLine(), out choice) && (choice == 1 || choice == 2))
            {
                break;
            }
            Console.WriteLine("Некорректный ввод. Пожалуйста, выберите 1 или 2.");
        }

        return choice == 1 ? "1.12.2" : "1.7.10";
    }

    // Метод для получения последнего краш-репорта
    static string GetLatestCrashReport(string crashDir)
    {
        if (!Directory.Exists(crashDir))
        {
            return null;
        }

        var crashFiles = Directory.GetFiles(crashDir, "*.txt")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .FirstOrDefault();

        return crashFiles?.FullName;
    }

    // Метод для загрузки лога на mclo.gs
    static async Task<string> UploadLogAsync(string logPath, string logName)
    {
        if (!File.Exists(logPath))
        {
            return $"Файл {logName} не найден.";
        }

        // Проверяем размер файла (максимум 10 МБ)
        var fileInfo = new FileInfo(logPath);
        if (fileInfo.Length > 10 * 1024 * 1024) // 10 МБ
        {
            return $"Файл {logName} слишком большой (более 10 МБ).";
        }

        string logContent = await File.ReadAllTextAsync(logPath, Encoding.UTF8);
        if (string.IsNullOrEmpty(logContent))
        {
            return $"Файл {logName} пустой.";
        }

        using (var client = new HttpClient())
        {
            using (var content = new MultipartFormDataContent())
            {
                content.Add(new StringContent(logContent), "content");

                var response = await client.PostAsync("https://api.mclo.gs/1/log", content);
                response.EnsureSuccessStatusCode();

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonResponse);

                if (result.ContainsKey("success") && result["success"].ToString() == "True")
                {
                    if (result.TryGetValue("url", out var url))
                    {
                        return url.ToString();
                    }
                    return $"URL для {logName} не найден в ответе API.";
                }
                else
                {
                    string errorMessage = result.TryGetValue("message", out var msg) ? msg.ToString() : "Неизвестная ошибка.";
                    return $"Не удалось загрузить {logName}: {errorMessage}";
                }
            }
        }
    }

    // Метод для отправки данных в Discord через WebHook
    static async Task SendToDiscordAsync(string webhookUrl, string json)
    {
        using (var client = new HttpClient())
        {
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(webhookUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                string errorDetails = await response.Content.ReadAsStringAsync();
                throw new Exception($"Не удалось отправить данные в Discord. Код: {response.StatusCode}. Ошибка: {errorDetails}");
            }
        }
    }

    // Метод для логирования действий программы в файл
    static void LogToFile(string message)
    {
        string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        File.AppendAllText(LogFilePath, logEntry + Environment.NewLine);
    }

    // Метод для получения ника с запоминанием
    static string GetNickname()
    {
        if (File.Exists(MemoryFilePath))
        {
            string savedNickname = File.ReadAllText(MemoryFilePath).Trim();
            if (!string.IsNullOrEmpty(savedNickname))
            {
                Console.WriteLine($"Используется сохранённый ник: {savedNickname}");
                LogToFile($"Используется сохранённый ник: {savedNickname}");
                return savedNickname;
            }
        }

        string nickname = GetUserInput("Введите ваш ник: ");
        File.WriteAllText(MemoryFilePath, nickname);
        LogToFile($"Ник сохранён: {nickname}");
        return nickname;
    }

    // Метод для получения ввода от пользователя с проверкой
    static string GetUserInput(string prompt)
    {
        string input;
        do
        {
            Console.Write(prompt);
            input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input))
            {
                Console.WriteLine("Ввод не может быть пустым. Попробуйте снова.");
            }
        } while (string.IsNullOrEmpty(input));
        return input;
    }
}