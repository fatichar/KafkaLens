using Android.App;
using Android.Runtime;
using Avalonia.Android;
using AvaloniaApp;
using System;

namespace AvaloniaApp.Android;

[Application]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }
}
