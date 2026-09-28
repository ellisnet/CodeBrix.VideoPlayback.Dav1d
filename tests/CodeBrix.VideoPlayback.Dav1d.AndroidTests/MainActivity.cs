using Android.App;
using Android.OS;
using Android.Widget;
using CodeBrix.Audio.Android;

namespace CodeBrix.VideoPlayback.Dav1d.AndroidTests;

/// <summary>Runs the packaged-library checks and leaves their results in the application's private files.</summary>
[Activity(Name = "com.codebrix.dav1d.tests.MainActivity", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    /// <inheritdoc />
    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        TextView status = new TextView(this) { Text = "Running dav1d validation…" };
        SetContentView(status);
        CodeBrixAndroidAudio.Initialize(this);
        string directory = FilesDir.AbsolutePath;
        bool audio = Intent.GetBooleanExtra("audio", true);
        bool audioOnly = Intent.GetBooleanExtra("audioOnly", false);
        File.WriteAllText(Path.Combine(directory, "results.txt"), "RUNNING\n");
        File.WriteAllText(Path.Combine(directory, "progress.txt"), "Starting\nCore: " +
            typeof(CodeBrix.Audio.Codecs.VorbisPacketCodecFactory).Assembly.FullName + "\n");
        _ = Task.Run(() =>
        {
            string result;
            try
            {
                foreach (string folder in new[] { "vectors", "media" })
                {
                    Directory.CreateDirectory(Path.Combine(directory, folder));
                    foreach (string file in Assets.List(folder))
                    {
                        using Stream source = Assets.Open(folder + "/" + file);
                        using Stream destination = File.Create(Path.Combine(directory, folder, file));
                        source.CopyTo(destination);
                    }
                }
                result = DeviceChecks.Run(directory, audio, audioOnly, message =>
                    File.AppendAllText(Path.Combine(directory, "progress.txt"), message + "\n"));
            }
            catch (Exception error)
            {
                result = "FAIL\n" + error;
            }
            File.WriteAllText(Path.Combine(directory, "results.txt"), result);
            Android.Util.Log.Info("Dav1dValidation", result);
            RunOnUiThread(() => status.Text = result);
        });
    }
}
