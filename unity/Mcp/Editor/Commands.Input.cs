using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Digitoy.Mcp
{
    // Sentetik pointer: uGUI (EventSystem raycaster'lari) + Physics/Physics2D collider'lari. KOORDINAT: mantiksal
    // nokta, sol-ust orijin, y asagi, Game View render cozunurlugunde (view.get) — screenshot(scale=1) pikseliyle birebir.
    public static partial class Commands
    {
        sealed class Pointer
        {
            public PointerEventData Data;
            public GameObject Target;        // raycast'in degdigi GO
            public GameObject Press;         // pointerDown'u isleyen
            public GameObject ClickTarget;   // IPointerClickHandler sahibi (down aninda)
            public GameObject Drag;          // IDragHandler sahibi
            public bool Dragging;
            public bool Missed;
            public int Handlers;
            public string Source;            // ui | physics2d | physics | none
        }

        static Pointer _ptr;

        static Vector2 ToScreen(float x, float y)
        {
            GetViewSize(out int w, out int h);
            if (x < 0 || y < 0 || x > w || y > h)
                throw new RpcError($"({x},{y}) ekran disinda; Game View {w}x{h} (view.get)");
            return new Vector2(x, h - y);
        }

        static EventSystem Es()
        {
            var es = EventSystem.current;
            if (es == null)
            {
                foreach (var go in AllGos())
                    if (go.GetComponent<EventSystem>() is EventSystem e) { es = e; break; }
            }
            return es;
        }

        // Raycast: once EventSystem (GraphicRaycaster/PhysicsRaycaster), yoksa kendi Physics2D/Physics taramasi.
        static void Raycast(Pointer p, Vector2 screen)
        {
            p.Target = null; p.Source = "none";
            var es = Es();
            if (es != null)
            {
                var results = new List<RaycastResult>();
                es.RaycastAll(p.Data, results);
                if (results.Count > 0)
                {
                    p.Data.pointerCurrentRaycast = results[0];
                    p.Target = results[0].gameObject;
                    p.Source = results[0].module is PhysicsRaycaster || results[0].module is Physics2DRaycaster ? "physics" : "ui";
                    return;
                }
            }
            var cam = Camera.main;
            if (cam == null) foreach (var c in Camera.allCameras) { cam = c; break; }
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(screen);
            var hit2 = Physics2D.GetRayIntersection(ray, Mathf.Infinity);
            if (hit2.collider != null)
            {
                p.Target = hit2.collider.gameObject; p.Source = "physics2d";
                p.Data.pointerCurrentRaycast = new RaycastResult { gameObject = p.Target, worldPosition = hit2.point, screenPosition = screen };
                return;
            }
            if (Physics.Raycast(ray, out var hit3))
            {
                p.Target = hit3.collider.gameObject; p.Source = "physics";
                p.Data.pointerCurrentRaycast = new RaycastResult { gameObject = p.Target, worldPosition = hit3.point, worldNormal = hit3.normal, screenPosition = screen };
            }
        }

        static bool Exec<T>(GameObject go, PointerEventData d, ExecuteEvents.EventFunction<T> fn, Pointer p) where T : IEventSystemHandler
        {
            if (go == null) return false;
            var handled = ExecuteEvents.ExecuteHierarchy(go, d, fn);
            if (handled != null) { p.Handlers++; return true; }
            return false;
        }

        static void PointerDown(float x, float y)
        {
            var screen = ToScreen(x, y);
            var es = Es();
            var p = new Pointer
            {
                Data = new PointerEventData(es)
                {
                    position = screen, pressPosition = screen, button = PointerEventData.InputButton.Left, pointerId = -1, clickCount = 1, clickTime = Time.unscaledTime,
                },
            };
            Raycast(p, screen);
            _ptr = p;
            if (p.Target == null) { p.Missed = true; return; }
            p.Data.pointerPressRaycast = p.Data.pointerCurrentRaycast;
            Exec(p.Target, p.Data, ExecuteEvents.pointerEnterHandler, p);
            p.Data.pointerEnter = p.Target;
            var press = ExecuteEvents.ExecuteHierarchy(p.Target, p.Data, ExecuteEvents.pointerDownHandler);
            if (press != null) p.Handlers++;
            p.ClickTarget = ExecuteEvents.GetEventHandler<IPointerClickHandler>(p.Target);
            p.Press = press ?? p.ClickTarget;
            p.Data.pointerPress = p.Press;
            p.Data.rawPointerPress = p.Target;
            p.Drag = ExecuteEvents.GetEventHandler<IDragHandler>(p.Target);
            p.Data.pointerDrag = p.Drag;
            if (p.Drag != null) Exec(p.Drag, p.Data, ExecuteEvents.initializePotentialDrag, p);
            if (es != null && ExecuteEvents.GetEventHandler<ISelectHandler>(p.Target) is GameObject sel)
                es.SetSelectedGameObject(sel, p.Data);
            if (p.Source != "ui")
                p.Target.SendMessage("OnMouseDown", SendMessageOptions.DontRequireReceiver); // legacy
        }

        static void PointerMove(float x, float y)
        {
            var p = _ptr ?? throw new RpcError("once down");
            var screen = ToScreen(x, y);
            p.Data.delta = screen - p.Data.position;
            p.Data.position = screen;
            if (p.Drag != null)
            {
                if (!p.Dragging)
                {
                    p.Dragging = true;
                    Exec(p.Drag, p.Data, ExecuteEvents.beginDragHandler, p);
                    p.Data.dragging = true;
                }
                Exec(p.Drag, p.Data, ExecuteEvents.dragHandler, p);
            }
            if (p.Target != null && p.Source != "ui") p.Target.SendMessage("OnMouseDrag", SendMessageOptions.DontRequireReceiver);
        }

        static void PointerUp(float x, float y)
        {
            var p = _ptr ?? throw new RpcError("once down");
            var screen = ToScreen(x, y);
            p.Data.position = screen;
            var under = new Pointer { Data = p.Data };
            Raycast(under, screen);
            Exec(p.Press, p.Data, ExecuteEvents.pointerUpHandler, p);
            var clickNow = ExecuteEvents.GetEventHandler<IPointerClickHandler>(under.Target);
            if (p.ClickTarget != null && clickNow == p.ClickTarget && !p.Dragging)
            {
                p.Data.eligibleForClick = true;
                if (ExecuteEvents.Execute(p.ClickTarget, p.Data, ExecuteEvents.pointerClickHandler)) p.Handlers++;
            }
            if (p.Dragging)
            {
                Exec(p.Drag, p.Data, ExecuteEvents.endDragHandler, p);
                if (under.Target != null) Exec(under.Target, p.Data, ExecuteEvents.dropHandler, p);
                p.Data.dragging = false;
            }
            Exec(p.Data.pointerEnter, p.Data, ExecuteEvents.pointerExitHandler, p);
            if (p.Target != null && p.Source != "ui")
            {
                p.Target.SendMessage("OnMouseUp", SendMessageOptions.DontRequireReceiver);
                if (under.Target == p.Target) p.Target.SendMessage("OnMouseUpAsButton", SendMessageOptions.DontRequireReceiver);
            }
        }

        static JNode GestureResult(string gesture)
        {
            var p = _ptr;
            var r = JNode.Map();
            r.Add("gesture", gesture);
            if (p == null || p.Missed || p.Target == null)
            {
                r.Add("hit", false);
                r.Add("note", "bu noktada raycast hedefi yok: UI icin Canvas'ta GraphicRaycaster + Graphic.raycastTarget=true, sahne nesnesi icin Collider/Collider2D gerekir; koordinat screenshot pikseliyle birebir (view.get)");
                return r;
            }
            r.Add("hit", true);
            r.Add("target", LivePath(p.Target));
            r.Add("targetId", p.Target.GetInstanceID());
            r.Add("source", p.Source);
            r.Add("handlers", p.Handlers);
            if (p.Handlers == 0)
                r.Add("note", "hit var ama EventSystem handler'i kosmadi: hedefte/atalarinda IPointerClickHandler vb. yok (Button/EventTrigger ya da kod)"
                    + (p.Source != "ui" ? ". Legacy OnMouseDown/Up SendMessage ile tetiklendi (sayilmaz)" : ""));
            return r;
        }

        static JNode RequirePlayForInput(Func<JNode> body)
        {
            var pre = EnsurePlaying(true, body);
            if (pre == null) return body();
            return IsDeferMarker(pre) ? null : pre;
        }

        [RpcCommand("input.pointer", "Ham pointer olayi (action: down|move|up; x,y mantiksal, sol-ust orijin). Play degilse baslatir")]
        static JNode InputPointer(string action, float x, float y)
        {
            return RequirePlayForInput(() =>
            {
                bool wasPaused = EditorApplication.isPaused;
                switch (action)
                {
                    case "down": PointerDown(x, y); break;
                    case "move": PointerMove(x, y); break;
                    case "up": PointerUp(x, y); break;
                    default: throw new RpcError("action down|move|up olmali");
                }
                int doneAt = Time.frameCount + 1;
                Advance();
                RpcHost.Defer(() =>
                {
                    if (Time.frameCount < doneAt) return null;
                    if (wasPaused) EditorApplication.isPaused = true;
                    return GestureResult(action);
                });
                return null;
            });
        }

        [RpcCommand("input.tap", "Tiklama: down, holdFrames frame sonra up, 1 frame sonra doner (handler'lar kostu). Play degilse baslatir; duraklatilmis play'de frame'leri kendisi adimlar")]
        static JNode InputTap(float x, float y, int holdFrames = 2)
        {
            return RequirePlayForInput(() =>
            {
                bool wasPaused = EditorApplication.isPaused;
                Time.captureDeltaTime = 1f / 60f;
                PointerDown(x, y);
                int upAt = Time.frameCount + Math.Max(1, holdFrames);
                bool upped = false;
                int doneAt = -1;
                Advance();
                RpcHost.Defer(() =>
                {
                    if (!Application.isPlaying) throw new RpcError("play bitti");
                    if (!upped)
                    {
                        if (Time.frameCount < upAt) return null;
                        PointerUp(x, y);
                        upped = true;
                        doneAt = Time.frameCount + 1;
                        return null;
                    }
                    if (Time.frameCount < doneAt) return null;
                    if (wasPaused) EditorApplication.isPaused = true;
                    Time.captureDeltaTime = 0f;
                    return GestureResult("click");
                });
                return null;
            });
        }

        [RpcCommand("input.drag", "Surukleme: (x0,y0) down, frames boyunca lineer move, (x1,y1) up; bitince doner")]
        static JNode InputDrag(float x0, float y0, float x1, float y1, int frames = 10)
        {
            if (frames < 1 || frames > 10000) throw new RpcError("frames 1..10000 olmali");
            return RequirePlayForInput(() =>
            {
                bool wasPaused = EditorApplication.isPaused;
                Time.captureDeltaTime = 1f / 60f;
                PointerDown(x0, y0);
                int start = Time.frameCount;
                int moved = 0;
                bool upped = false;
                int doneAt = -1;
                Advance();
                RpcHost.Defer(() =>
                {
                    if (!Application.isPlaying) throw new RpcError("play bitti");
                    if (!upped)
                    {
                        int i = Time.frameCount - start;
                        if (i > moved && moved < frames)
                        {
                            moved = i;
                            float t = Mathf.Clamp01(moved / (float)frames);
                            PointerMove(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t);
                        }
                        if (moved < frames) return null;
                        PointerUp(x1, y1);
                        upped = true;
                        doneAt = Time.frameCount + 1;
                        return null;
                    }
                    if (Time.frameCount < doneAt) return null;
                    if (wasPaused) EditorApplication.isPaused = true;
                    Time.captureDeltaTime = 0f;
                    return GestureResult("drag");
                });
                return null;
            });
        }
    }
}
