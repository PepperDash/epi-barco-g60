using System;
using System.Collections.Generic;
using PepperDash.Essentials.Core.DeviceTypeInterfaces;

namespace PepperDash.Essentials.Plugins.Barco.G60
{
  public class BarcoG60Inputs : ISelectableItems<string>
  {
    private Dictionary<string, ISelectableItem> items = new Dictionary<string, ISelectableItem>();

    public Dictionary<string, ISelectableItem> Items
    {
      get { return items; }
      set
      {
        if (value == items)
        {
          return;
        }
        items = value;

        ItemsUpdated?.Invoke(this, EventArgs.Empty);
      }
    }

    private string currentItem;

    public string CurrentItem
    {
      get => currentItem;
      set
      {
        if (currentItem == value)
        {
          return;
        }
        currentItem = value;

        CurrentItemChanged?.Invoke(this, EventArgs.Empty);
      }
    }

    public event EventHandler ItemsUpdated;
    public event EventHandler CurrentItemChanged;
  }

  public class BarcoG60Input : ISelectableItem
  {
    public string Key { get; set; }
    public string Name { get; set; }

    private bool isSelected = false;
    public bool IsSelected
    {
      get { return isSelected; }
      set
      {
        if (isSelected == value)
        {
          return;
        }

        isSelected = value;

        ItemUpdated?.Invoke(this, EventArgs.Empty);
      }
    }

    private readonly Action inputMethod;

    public BarcoG60Input(string key, string name, Action inputMethod)
    {
      Key = key;
      Name = name;
      this.inputMethod = inputMethod;

    }

    public event EventHandler ItemUpdated;

    public override string ToString()
    {
      return Name;
    }

    public void Select()
    {
      inputMethod?.Invoke();
    }
  }
}
