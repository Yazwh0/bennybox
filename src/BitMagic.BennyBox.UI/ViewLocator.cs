using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using BitMagic.BennyBox.ViewModels;

namespace BitMagic.BennyBox;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
        {
            return null;
        }

        var viewTypeName = param.GetType().FullName!.Replace("ViewModels", "Views").Replace("ViewModel", "View");

        // Type.GetType(string) only searches the calling assembly (this one) plus core assemblies by
        // default - it can't see Views in whichever head assembly (desktop, Android, ...) is actually
        // running, so every loaded assembly needs to be searched explicitly instead.
        var viewType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(viewTypeName))
            .FirstOrDefault(type => type is not null);

        if (viewType is not null)
        {
            return (Control)Activator.CreateInstance(viewType)!;
        }

        return new TextBlock { Text = $"View not found: {viewTypeName}" };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
