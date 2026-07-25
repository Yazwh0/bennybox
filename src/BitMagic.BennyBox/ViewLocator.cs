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
        var viewType = Type.GetType(viewTypeName);

        if (viewType is not null)
        {
            return (Control)Activator.CreateInstance(viewType)!;
        }

        return new TextBlock { Text = $"View not found: {viewTypeName}" };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
