using System;
using System.Collections.Generic;
using System.Linq;

namespace JPEG.Utilities;

static class IEnumerableExtensions
{
	public static T MinOrDefault<T>(this IEnumerable<T> enumerable, Func<T, int> selector)
	{
		var min = default(T);
		var minValue = int.MaxValue;
		var hasItems = false;
		foreach (var item in enumerable)
		{
			var val = selector(item);
			if (!hasItems || val < minValue)
			{
				hasItems = true;
				min = item;
				minValue = val;
			}
		}
		return min;
	}

	public static IEnumerable<T> Without<T>(this IEnumerable<T> enumerable, params T[] elements)
	{
		var excluded = new HashSet<T>(elements);
		foreach (var item in enumerable)
			if (!excluded.Contains(item))
				yield return item;
	}

	public static IEnumerable<T> ToEnumerable<T>(this T element)
	{
		yield return element;
	}
}