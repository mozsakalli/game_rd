using UnityEngine;
using System.Collections.Generic;
public class BoardGame<T> : MonoBehaviour where T : BoardPiece
{
    public BoardZone[] zones;
    public T piecePrefab;
    List<T> _piecePool = new();

    public T AllocatePiece()
    {
        if (_piecePool.Count > 0)
        {
            var piece = _piecePool[0];
            _piecePool.RemoveAt(0);
            return piece;
        }
        return Instantiate(piecePrefab);
    }

    public void ReleasePiece(T piece)
    {
        if (piece != null)
            _piecePool.Add(piece);
    }

}
