using System;

namespace PrestigePerks.Model
{
    /// <summary>
    /// 특전 목록 줄 자리 (두 열, 열 우선). 열당 줄 수는 최소 10 이고, 넘치면 두 열이 반씩 나눠 길어진다 (스크롤).
    /// 내장 20개는 지금 화면과 같다 (dev-docs 95 결정 2).
    /// </summary>
    public static class PerkListLayout
    {
        public const int VisibleRows = 10;
        public const float Top = 58, RowHeight = 62, RowGap = 3, Bottom = 8;

        public static int RowsPerColumn(int count) => Math.Max(VisibleRows, (count + 1) / 2);

        public static bool Overflows(int count) => RowsPerColumn(count) > VisibleRows;

        public static (int Column, int Row) Place(int index, int rowsPerColumn) => (index / rowsPerColumn, index % rowsPerColumn);

        /// <summary>목록 콘텐츠 높이 (머리 자리 포함, 패널 위에서부터).</summary>
        public static float ContentHeight(int rowsPerColumn) => Top + rowsPerColumn * RowHeight + (rowsPerColumn - 1) * RowGap + Bottom;
    }
}
