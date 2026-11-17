using Android.App;
using Android.Runtime;
using Avalonia.Android;
using KafkaLens.UI;
using System;

namespace KafkaLens.Android;

[Application]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }
}
