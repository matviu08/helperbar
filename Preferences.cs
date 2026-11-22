using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private TextBlock? shortcutFooter;
    private Button? petHeaderButton;
    private bool capturingShortcut;
    private void UpdateShortcutLabels()
    {
        if (shortcutFooter != null) shortcutFooter.Text = shortcuts?.IsRegistered("open") == true ? settings.OpenShortcut : "Відкрити: клік або трей";
        if (petHeaderButton != null) petHeaderButton.ToolTip = "Залишити лише очі · " + settings.PetShortcut;
        if (tray != null) tray.Text = "HelperBar · " + settings.OpenShortcut;
    }
    private FrameworkElement DailyPreferences()
    {
        var stack = new StackPanel();
        var enabled = !smokeTest && StartupService.IsEnabled();
        CheckBox? startup = null; var updating = false;
        startup = Ui.Toggle("Запускати разом із Windows", enabled, value =>
        {
            if (updating) return;
            if (smokeTest) { updating = true; startup!.IsChecked = false; updating = false; return; }
            if (!StartupService.SetEnabled(value, out var message)) { updating = true; startup!.IsChecked = !value; updating = false; }
            surface.Notify(message, "curious");
        });
        stack.Children.Add(SettingsGroup("Запуск і поведінка", startup,
            Ui.Toggle("Відкривати після наведення", settings.OpenOnHover, value => { settings.OpenOnHover = value; Persist(); }),
            Ui.Toggle("Секунди в годиннику", settings.ShowSeconds, value => { settings.ShowSeconds = value; Persist(); }),
            Ui.Toggle("Звук завершення таймерів", settings.TimerSound, value => { settings.TimerSound = value; Persist(); })));
        stack.Children.Add(SettingsGroup("Гарячі клавіші", ShortcutEditor("Відкрити / згорнути", "open", settings.OpenShortcut), Ui.Gap(7),
            ShortcutEditor("Лише очі", "pet", settings.PetShortcut), Ui.Gap(5),
            Ui.Text("Натисни комбінацію, потім нові клавіші. Esc — скасувати.", 10, "#887B98")));
        stack.Children.Add(Ui.Text("Налаштування зберігаються автоматично на цьому комп’ютері.", 10, "#887B98"));
        return stack;
    }
    private FrameworkElement ShortcutEditor(string title, string action, string current)
    {
        Button? button = null;
        button = Ui.Button(current, () => { capturingShortcut = true; button!.Content = "Натисни клавіші…"; button.Focus(); }, "Змінити: " + title);
        button.MinHeight = 30; button.FontSize = 11; button.Padding = new Thickness(9, 5, 9, 5);
        var status = Ui.Text(shortcuts?.IsRegistered(action) == false ? "Комбінацію зайнято. Вибери іншу." : "", 10, "#E8BB95");
        button.PreviewKeyDown += (_, e) =>
        {
            if (!capturingShortcut) return;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { capturingShortcut = false; button.Content = current; return; }
            if (!Shortcut.AllowedKey(key)) return;
            var modifiers = Keyboard.Modifiers;
            uint flags = ((modifiers & ModifierKeys.Control) != 0 ? 2u : 0) | ((modifiers & ModifierKeys.Alt) != 0 ? 1u : 0) | ((modifiers & ModifierKeys.Shift) != 0 ? 4u : 0) | ((modifiers & ModifierKeys.Windows) != 0 ? 8u : 0);
            if (flags == 0) { status.Text = "Додай Ctrl, Alt, Shift або Win."; return; }
            var shortcut = new Shortcut(flags, (uint)KeyInterop.VirtualKeyFromKey(key));
            if (shortcuts?.Set(action, shortcut) != true)
            { status.Text = "Комбінацію зайнято. Попередня залишається."; return; }
            current = shortcut.ToString(); button.Content = current; status.Text = "Збережено"; capturingShortcut = false;
            if (action == "open") settings.OpenShortcut = current; else settings.PetShortcut = current;
            Persist(); UpdateShortcutLabels();
        };
        button.LostKeyboardFocus += (_, _) => { capturingShortcut = false; button.Content = current; };
        return Ui.Column(Ui.Columns(Ui.Text(title, 11, "#BFB2CF"), button), status);
    }
}
