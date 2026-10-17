using System.Windows;
using System.Windows.Controls;

namespace Absolute.App.Controls;

/// <summary>Bounded cards with each incomplete row centered, including a single last card.</summary>
internal sealed class CenteredCardsPanel:Panel
{
    public int Columns{get;init;}=2;
    public double CardWidth{get;init;}=340;
    public double Gap{get;init;}=18;
    private int columns;
    private double cellWidth;
    private readonly List<double> heights=[];

    protected override Size MeasureOverride(Size available)
    {
        var width=double.IsInfinity(available.Width)?Columns*CardWidth+(Columns-1)*Gap:available.Width;
        columns=Math.Max(1,Math.Min(Columns,(int)Math.Floor((width+Gap)/(Math.Min(CardWidth,Columns>=3?210:250)+Gap))));
        cellWidth=Math.Min(CardWidth,Math.Max(0,(width-(columns-1)*Gap)/columns));
        heights.Clear();
        for(var i=0;i<InternalChildren.Count;i++)
        {
            var child=InternalChildren[i];child.Measure(new Size(cellWidth,double.PositiveInfinity));
            var row=i/columns;if(row==heights.Count)heights.Add(0);
            heights[row]=Math.Max(heights[row],child.DesiredSize.Height);
        }
        return new Size(width,heights.Sum()+Math.Max(0,heights.Count-1)*Gap);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var y=0d;
        for(var row=0;row<heights.Count;row++)
        {
            var count=Math.Min(columns,InternalChildren.Count-row*columns);
            var x=Math.Max(0,(finalSize.Width-(count*cellWidth+(count-1)*Gap))/2);
            for(var i=0;i<count;i++)InternalChildren[row*columns+i].Arrange(new Rect(x+i*(cellWidth+Gap),y,cellWidth,heights[row]));
            y+=heights[row]+Gap;
        }
        return finalSize;
    }
}
