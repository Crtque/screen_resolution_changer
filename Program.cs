using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

class Program
{
    // ====== i18n словари ======
    static Dictionary<string, Dictionary<string, string>> translations = new()
    {
        ["en"] = new Dictionary<string, string>
        {
            ["current_mode"] = "Current resolution: {0}x{1} @ {2}Hz, {3} bpp\n",
            ["available_modes"] = "Available refresh rates:",
            ["no_modes"] = "No alternative refresh rates found for current resolution.",
            ["choose"] = "Choose menu item (number): ",
            ["invalid_choice"] = "Invalid choice.",
            ["applying"] = "\nApplying {0} Hz...",
            ["success"] = "Refresh rate changed successfully.",
            ["restart"] = "Settings saved. System restart required to apply.",
            ["fail_badmode"] = "Driver rejected the mode (BADMODE).",
            ["fail_test"] = "Test of mode failed. Code: {0}",
            ["fail_apply"] = "Could not apply mode. Code: {0}",
            ["hz_not_available"] = "Requested frequency {0} Hz is not available."
        },
        ["ru"] = new Dictionary<string, string>
        {
            ["current_mode"] = "Текущее разрешение: {0}x{1} @ {2}Гц, {3} bpp\n",
            ["available_modes"] = "Доступные частоты обновления:",
            ["no_modes"] = "Не найдено альтернативных частот обновления для текущего разрешения.",
            ["choose"] = "Выберите пункт меню (число): ",
            ["invalid_choice"] = "Некорректный выбор.",
            ["applying"] = "\nПрименяю {0} Гц...",
            ["success"] = "Частота успешно изменена.",
            ["restart"] = "Параметры сохранены. Для применения требуется перезагрузка системы.",
            ["fail_badmode"] = "Драйвер отклонил режим (BADMODE).",
            ["fail_test"] = "Тест режима не прошёл. Код: {0}",
            ["fail_apply"] = "Не удалось применить режим. Код: {0}",
            ["hz_not_available"] = "Запрошенная частота {0} Гц недоступна."
        }
    };

    static string lang = "en"; // default
    static Dictionary<string, string> T => translations.ContainsKey(lang) ? translations[lang] : translations["en"];
    static string _(string key, params object[] args) => 
        T.ContainsKey(key) ? string.Format(T[key], args) : key;

    // ====== WinAPI ======
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DEVMODE
    {
        private const int CCHDEVICENAME = 32;
        private const int CCHFORMNAME = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll")] public static extern int EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);
    [DllImport("user32.dll")] public static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);

    const int ENUM_CURRENT_SETTINGS = -1;
    const int DM_BITSPERPEL = 0x00040000, DM_PELSWIDTH = 0x00080000, DM_PELSHEIGHT = 0x00100000, DM_DISPLAYFREQUENCY = 0x00400000;
    const int CDS_TEST = 0x00000002, CDS_UPDATEREGISTRY = 0x00000001;
    const int DISP_CHANGE_SUCCESSFUL = 0, DISP_CHANGE_RESTART = 1, DISP_CHANGE_BADMODE = -2;

    static void Main(string[] args)
    {
        int? targetHzArg = null;

        // --- аргументы: язык и частота
        for (int i = 0; i < args.Length; i++)
        {
            if (translations.ContainsKey(args[i]))
            {
                lang = args[i];
            }
            else if (args[i] == "-hz" && i + 1 < args.Length && int.TryParse(args[i + 1], out int hz))
            {
                targetHzArg = hz;
                i++;
            }
        }

        var current = CreateDevMode();
        if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref current) == 0)
        {
            Console.WriteLine("Failed to get display settings.");
            return;
        }

        Console.WriteLine(_(
            "current_mode",
            current.dmPelsWidth,
            current.dmPelsHeight,
            current.dmDisplayFrequency,
            current.dmBitsPerPel));

        var refreshRates = GetAvailableFrequencies(current.dmPelsWidth, current.dmPelsHeight, current.dmBitsPerPel);

        if (refreshRates.Count == 0)
        {
            Console.WriteLine(_("no_modes"));
            return;
        }

        // --- если частота задана через аргумент
        if (targetHzArg.HasValue)
        {
            if (refreshRates.Contains(targetHzArg.Value))
            {
                ApplyFrequency(current, targetHzArg.Value);
            }
            else
            {
                Console.WriteLine(_("hz_not_available", targetHzArg.Value));
            }
            return;
        }

        // --- иначе меню
        Console.WriteLine(_("available_modes"));
        for (int i = 0; i < refreshRates.Count; i++)
            Console.WriteLine($"{i + 1} - {refreshRates[i]} Hz");

        Console.Write(_("choose"));
        string? input = Console.ReadLine();
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > refreshRates.Count)
        {
            Console.WriteLine(_("invalid_choice"));
            return;
        }

        ApplyFrequency(current, refreshRates[choice - 1]);
    }

    static void ApplyFrequency(DEVMODE current, int hz)
    {
        Console.WriteLine(_("applying", hz));

        var newMode = current;
        newMode.dmDisplayFrequency = hz;
        newMode.dmFields = DM_DISPLAYFREQUENCY | DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL;

        int test = ChangeDisplaySettings(ref newMode, CDS_TEST);
        if (test != DISP_CHANGE_SUCCESSFUL)
        {
            Console.WriteLine(test == DISP_CHANGE_BADMODE ? _("fail_badmode") : _("fail_test", test));
            return;
        }

        int apply = ChangeDisplaySettings(ref newMode, CDS_UPDATEREGISTRY);
        if (apply == DISP_CHANGE_SUCCESSFUL)
            Console.WriteLine(_("success"));
        else if (apply == DISP_CHANGE_RESTART)
            Console.WriteLine(_("restart"));
        else
            Console.WriteLine(_("fail_apply", apply));
    }

    static DEVMODE CreateDevMode()
    {
        var dm = new DEVMODE();
        dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        return dm;
    }

    static List<int> GetAvailableFrequencies(int width, int height, int bpp)
    {
        var set = new HashSet<int>();
        int modeNum = 0;
        var dm = CreateDevMode();

        while (EnumDisplaySettings(null, modeNum, ref dm) != 0)
        {
            if (dm.dmPelsWidth == width && dm.dmPelsHeight == height && dm.dmBitsPerPel == bpp && dm.dmDisplayFrequency > 0)
                set.Add(dm.dmDisplayFrequency);
            modeNum++;
        }

        var list = set.ToList();
        list.Sort((a, b) => b.CompareTo(a)); // по убыванию
        return list;
    }
}
