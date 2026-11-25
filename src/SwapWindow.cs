// Loco Tester - "Replace sounds" window: one clean card per sound (horn, bell, sander) with
// Preview, Replace… and Original buttons.
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LocoTester
{
    public class SwapWindow : Window
    {
        readonly Loco loco;
        readonly string orRoot;
        readonly AudioEngine audio;
        readonly StackPanel rows = new StackPanel();
        readonly CheckBox addLoop;
        readonly TextBlock status;
        readonly object previewTag = new object();
        /// <summary>Set when a sound was changed, so the main window reloads the loco's sound.</summary>
        public bool Changed;

        public SwapWindow(Window owner, Loco l, string orRoot, AudioEngine audio)
        {
            loco = l; this.orRoot = orRoot; this.audio = audio;
            Owner = owner;
            Title = "Replace sounds · " + l.DisplayName;
            Width = 640; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Theme.B("#0A111D"); Foreground = Theme.B("#E6EDF7");
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"); FontSize = 13;
            Resources = owner.Resources;   // same buttons / cards as the main window

            StackPanel root = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
            root.Children.Add(new TextBlock { Text = "Replace sounds", FontSize = 20, FontWeight = FontWeights.Bold });
            root.Children.Add(new TextBlock
            {
                Text = "Pick your own sound for the horn, bell or sander. Use .wav files only (PCM WAV: Open Rails and MSTS do not play MP3 or OGG). " +
                       "Loco Tester copies it next to the loco's sound file and points the sound file at it. " +
                       "The untouched original is kept, so ↺ Original puts it back at any time.",
                Foreground = Theme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 14), FontSize = 12.5
            });
            root.Children.Add(rows);

            addLoop = new CheckBox
            {
                IsChecked = true, Foreground = Theme.B("#C9D5E8"), Margin = new Thickness(2, 12, 0, 0),
                Content = new TextBlock { Text = "Make it sustain while the button is held (adds loop points to a .wav that has none, like MSTS horns)", TextWrapping = TextWrapping.Wrap }
            };
            root.Children.Add(addLoop);

            status = new TextBlock { Foreground = Theme.Teal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 12, 0, 0), FontSize = 12.5 };
            root.Children.Add(status);

            Button close = new Button { Content = "Done", Padding = new Thickness(22, 8, 22, 8), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            close.Style = (Style)owner.FindResource("Primary");
            close.Click += delegate { Close(); };
            root.Children.Add(close);
            Content = root;
            Closed += delegate { audio.StopTag(previewTag); };
            Fill();
        }

        void Fill()
        {
            rows.Children.Clear();
            List<SwapSlot> slots;
            try { slots = SoundSwap.Slots(loco); }
            catch (Exception ex) { status.Foreground = Theme.Red; status.Text = ex.Message; return; }
            if (slots.Count == 0)
            {
                rows.Children.Add(new TextBlock { Text = "This loco's sound files have no horn, bell or sander sound to replace.", Foreground = Theme.Muted });
                return;
            }
            foreach (SwapSlot s in slots) rows.Children.Add(Card(s));
        }

        UIElement Card(SwapSlot s)
        {
            Border card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10) };
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border badge = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Background = Theme.B("#1D2D49"), VerticalAlignment = VerticalAlignment.Center };
            badge.Child = new TextBlock { Text = s.Glyph, FontSize = 19, FontFamily = new FontFamily("Segoe UI Emoji"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(badge);

            StackPanel info = new StackPanel { Margin = new Thickness(12, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            StackPanel head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new TextBlock { Text = s.Title, FontSize = 14.5, FontWeight = FontWeights.SemiBold });
            Border chip = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = s.Replaced ? Theme.B("#33F7811E") : Theme.B("#2222C3B0") };
            chip.Child = new TextBlock { Text = s.Replaced ? "Replaced" : "Original", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = s.Replaced ? Theme.Accent : Theme.Teal };
            head.Children.Add(chip);
            info.Children.Add(head);
            info.Children.Add(new TextBlock { Text = string.Join(",  ", s.CurrentFiles.ToArray()), Foreground = Theme.B("#AEBBD2"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) });
            List<string> names = new List<string>(); foreach (string f in s.SmsFiles) names.Add(Path.GetFileName(f));
            info.Children.Add(new TextBlock { Text = "in " + string.Join(", ", names.ToArray()), Foreground = Theme.B("#6F82A3"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(info, 1);
            g.Children.Add(info);

            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Button play = new Button { Content = "▶", Padding = new Thickness(11, 6, 11, 6), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Preview the current sound" };
            play.Click += delegate { Preview(s); };
            Button rep = new Button { Content = "Replace…", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 6, 0) };
            rep.Style = (Style)FindResource("Primary");
            rep.Click += delegate { Replace(s); };
            Button orig = new Button { Content = "↺ Original", Padding = new Thickness(10, 6, 10, 6), IsEnabled = s.Replaced, ToolTip = s.Replaced ? "Put the original sound back" : "Already the original sound" };
            orig.Click += delegate { Do(delegate { return SoundSwap.Restore(loco, s); }); };
            btns.Children.Add(play); btns.Children.Add(rep); btns.Children.Add(orig);
            Grid.SetColumn(btns, 2);
            g.Children.Add(btns);
            card.Child = g;
            return card;
        }

        void Preview(SwapSlot s)
        {
            audio.StopTag(previewTag);
            foreach (string sms in s.SmsFiles)
            {
                SmsFile f = new SmsFile { Path = sms, Folder = Path.GetDirectoryName(sms) };
                foreach (string file in s.CurrentFiles)
                {
                    string p = f.ResolveWav(file, orRoot);
                    if (p == null) continue;
                    WavData w = WavData.Get(p);
                    if (w.Error != null) { status.Foreground = Theme.Red; status.Text = file + ": " + w.Error; return; }
                    audio.Play(w, false, 0.5f, 1, previewTag);
                    status.Foreground = Theme.Muted; status.Text = "Playing " + file;
                    return;
                }
            }
            status.Foreground = Theme.Amber; status.Text = "The sound file could not be found.";
        }

        void Replace(SwapSlot s)
        {
            Microsoft.Win32.OpenFileDialog d = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose the new " + s.Title.ToLower() + " sound",
                Filter = "WAV sound (*.wav)|*.wav",
                CheckFileExists = true
            };
            if (d.ShowDialog(this) != true) return;
            string err = SoundSwap.Check(d.FileName);
            if (err != null) { status.Foreground = Theme.Red; status.Text = Path.GetFileName(d.FileName) + ": " + err; return; }
            bool loop = addLoop.IsChecked == true;
            string file = d.FileName;
            Do(delegate { return SoundSwap.Replace(loco, s, file, loop); });
        }

        void Do(Func<string> action)
        {
            audio.StopTag(previewTag);
            try
            {
                string msg = action();
                WavData.ClearCache();
                Changed = true;
                status.Foreground = Theme.Teal; status.Text = "✓  " + msg;
            }
            catch (Exception ex) { status.Foreground = Theme.Red; status.Text = ex.Message; }
            Fill();
        }
    }
}
