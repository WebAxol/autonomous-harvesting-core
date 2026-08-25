using System;
using System.Collections.Generic;
using System.Text;
using HarvestingCore.Configuration;

namespace HarvestingCore.World
{
    /// <summary>
    /// Owns grid dimensions, the cell matrix, refuel station positions, and dump
    /// site positions (Glossary: World_Model).
    /// </summary>
    public sealed class WorldModel
    {
        private readonly Cell[] _cells;                    // flat, row-major: index = y * Width + x
        private readonly List<GridPosition> _refuelStations;
        private readonly List<GridPosition> _dumpSites;

        public int Width { get; }
        public int Height { get; }
        public bool IsGenerated { get; private set; }
        public IReadOnlyList<Cell> Cells { get; }
        public IReadOnlyList<GridPosition> RefuelStations { get; }
        public IReadOnlyList<GridPosition> DumpSites { get; }

        public WorldModel(int width, int height, IEnumerable<GridPosition> refuelStations,
            IEnumerable<GridPosition> dumpSites)
        {
            if (width < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "width must be at least 1.");
            }
            if (height < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(height), "height must be at least 1.");
            }

            Width = width;
            Height = height;

            _cells = new Cell[width * height];
            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i] = new Cell();
            }
            Cells = Array.AsReadOnly(_cells);

            _refuelStations = ValidatePositions(refuelStations, nameof(refuelStations));
            _dumpSites = ValidatePositions(dumpSites, nameof(dumpSites));
            RefuelStations = _refuelStations.AsReadOnly();
            DumpSites = _dumpSites.AsReadOnly();

            IsGenerated = false;
        }

        private List<GridPosition> ValidatePositions(IEnumerable<GridPosition> positions, string collectionName)
        {
            var list = new List<GridPosition>();
            if (positions == null)
            {
                return list;
            }

            var seen = new HashSet<GridPosition>();
            foreach (var position in positions)
            {
                if (!InBounds(position))
                {
                    throw new ArgumentException(
                        "Position " + position + " in " + collectionName + " is out of bounds.",
                        collectionName);
                }
                if (!seen.Add(position))
                {
                    throw new ArgumentException(
                        "Position " + position + " in " + collectionName + " is duplicated.",
                        collectionName);
                }
                list.Add(position);
            }
            return list;
        }

        public bool InBounds(GridPosition p)
        {
            return p.X >= 0 && p.X < Width && p.Y >= 0 && p.Y < Height;
        }

        public int IndexOf(GridPosition p)
        {
            return p.Y * Width + p.X;
        }

        public GridPosition PositionOf(int index)
        {
            int y = index / Width;
            int x = index % Width;
            return new GridPosition(x, y);
        }

        public Cell CellAt(GridPosition p)
        {
            if (p.X < 0 || p.X >= Width)
            {
                throw new ArgumentOutOfRangeException(nameof(p), "x is out of bounds.");
            }
            if (p.Y < 0 || p.Y >= Height)
            {
                throw new ArgumentOutOfRangeException(nameof(p), "y is out of bounds.");
            }
            return _cells[IndexOf(p)];
        }

        public bool TryGetCell(GridPosition p, out Cell cell)
        {
            if (!InBounds(p))
            {
                cell = null;
                return false;
            }
            cell = _cells[IndexOf(p)];
            return true;
        }

        public bool IsPassable(GridPosition p)
        {
            return InBounds(p) && _cells[IndexOf(p)].State != CellState.Blocked;
        }

        /// <summary>
        /// Generates the grid using SimulationConfig.Default's densities (Req 1.6, 1.7, 1.8).
        /// </summary>
        public bool Generate(IRandomSource random)
        {
            return Generate(random, SimulationConfig.Default);
        }

        /// <summary>
        /// Generates the grid using the supplied config's CropDensity/BlockedDensity.
        /// Returns false and leaves the matrix untouched when already generated.
        /// Walks the flat array in row-major index order, drawing once per cell, so two
        /// identical seeds produce identical matrices (Req 1.8). Refuel and dump positions
        /// are forced to Empty afterwards so stations are never unreachable by construction.
        /// </summary>
        public bool Generate(IRandomSource random, SimulationConfig config)
        {
            if (IsGenerated)
            {
                return false;
            }

            for (int index = 0; index < _cells.Length; index++)
            {
                double draw = random.NextDouble();
                CellState state;
                if (draw < config.CropDensity)
                {
                    state = CellState.Crop;
                }
                else if (draw < config.CropDensity + config.BlockedDensity)
                {
                    state = CellState.Blocked;
                }
                else
                {
                    state = CellState.Empty;
                }
                _cells[index].SetStateForGeneration(state);
            }

            foreach (var station in _refuelStations)
            {
                _cells[IndexOf(station)].SetStateForGeneration(CellState.Empty);
            }
            foreach (var dump in _dumpSites)
            {
                _cells[IndexOf(dump)].SetStateForGeneration(CellState.Empty);
            }

            IsGenerated = true;
            return true;
        }

        /// <summary>Char-grid form: '.' empty, 'W' crop, '#' blocked, '_' harvested.</summary>
        public string Serialize()
        {
            var builder = new StringBuilder(Width * (Height + 1));
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    var cell = _cells[IndexOf(new GridPosition(x, y))];
                    builder.Append(CharForState(cell.State));
                }
                if (y < Height - 1)
                {
                    builder.Append('\n');
                }
            }
            return builder.ToString();
        }

        private static char CharForState(CellState state)
        {
            switch (state)
            {
                case CellState.Empty: return '.';
                case CellState.Crop: return 'W';
                case CellState.Blocked: return '#';
                case CellState.Harvested: return '_';
                default: throw new ArgumentOutOfRangeException(nameof(state));
            }
        }

        private static CellState StateForChar(char c)
        {
            switch (c)
            {
                case '.': return CellState.Empty;
                case 'W': return CellState.Crop;
                case '#': return CellState.Blocked;
                case '_': return CellState.Harvested;
                default:
                    throw new ArgumentException("Unrecognised cell character '" + c + "'.", nameof(c));
            }
        }

        /// <summary>Inverse of Serialize(): builds a generated WorldModel from char-grid text.</summary>
        public static WorldModel Parse(string text, IEnumerable<GridPosition> refuel,
            IEnumerable<GridPosition> dumps)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            string[] rows = text.Split('\n');
            int height = rows.Length;
            int width = height > 0 ? rows[0].Length : 0;

            var model = new WorldModel(width, height, refuel, dumps);

            for (int y = 0; y < height; y++)
            {
                string row = rows[y];
                for (int x = 0; x < width; x++)
                {
                    CellState state = StateForChar(row[x]);
                    model._cells[model.IndexOf(new GridPosition(x, y))].SetStateForGeneration(state);
                }
            }

            model.IsGenerated = true;
            return model;
        }
    }
}
