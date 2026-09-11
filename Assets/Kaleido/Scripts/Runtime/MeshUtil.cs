using System.Collections.Generic;
using UnityEngine;

namespace Kaleido
{
    /// <summary>Procedural meshes: inward-facing dome sphere, floor ring, dot quads, polygons.</summary>
    public static class MeshUtil
    {
        /// <summary>Full UV sphere with inward-facing winding (we sit inside it).</summary>
        public static Mesh InwardSphere(float radius, int longitudeSegments = 96, int latitudeSegments = 48)
        {
            var mesh = new Mesh { name = "InwardSphere", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (int lat = 0; lat <= latitudeSegments; lat++)
            {
                float v = (float)lat / latitudeSegments;
                float phi = v * Mathf.PI;               // 0 = zenith
                float y = Mathf.Cos(phi);
                float r = Mathf.Sin(phi);
                for (int lon = 0; lon <= longitudeSegments; lon++)
                {
                    float u = (float)lon / longitudeSegments;
                    float theta = u * Mathf.PI * 2f;
                    var dir = new Vector3(r * Mathf.Sin(theta), y, r * Mathf.Cos(theta));
                    verts.Add(dir * radius);
                    normals.Add(-dir);
                    uvs.Add(new Vector2(u, 1f - v));
                }
            }
            var tris = new List<int>();
            int stride = longitudeSegments + 1;
            for (int lat = 0; lat < latitudeSegments; lat++)
            {
                for (int lon = 0; lon < longitudeSegments; lon++)
                {
                    int a = lat * stride + lon;
                    int b = a + stride;
                    // inward winding (reversed relative to an outward sphere)
                    tris.Add(a); tris.Add(a + 1); tris.Add(b);
                    tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
                }
            }
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * radius * 2.2f);
            return mesh;
        }

        /// <summary>Flat annulus on XZ, uv.x = 0 at inner radius, 1 at outer radius.</summary>
        public static Mesh Ring(float inner, float outer, int segments = 96)
        {
            var mesh = new Mesh { name = "Ring" };
            var verts = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var tris = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float a = (float)i / segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts[i * 2] = dir * inner;
                verts[i * 2 + 1] = dir * outer;
                uvs[i * 2] = new Vector2(0f, (float)i / segments);
                uvs[i * 2 + 1] = new Vector2(1f, (float)i / segments);
            }
            for (int i = 0; i < segments; i++)
            {
                int b = i * 2;
                tris[i * 6 + 0] = b; tris[i * 6 + 1] = b + 2; tris[i * 6 + 2] = b + 1;
                tris[i * 6 + 3] = b + 1; tris[i * 6 + 4] = b + 2; tris[i * 6 + 5] = b + 3;
            }
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>N unit quads (uv in [-1,1]) with uv2.x = index; positions are computed in the vertex shader.</summary>
        public static Mesh IndexedQuads(int count, float boundsRadius)
        {
            var mesh = new Mesh { name = "IndexedQuads", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var verts = new Vector3[count * 4];
            var uv = new Vector2[count * 4];
            var uv2 = new Vector2[count * 4];
            var tris = new int[count * 6];
            for (int i = 0; i < count; i++)
            {
                int b = i * 4;
                uv[b + 0] = new Vector2(-1, -1);
                uv[b + 1] = new Vector2(1, -1);
                uv[b + 2] = new Vector2(1, 1);
                uv[b + 3] = new Vector2(-1, 1);
                for (int k = 0; k < 4; k++) uv2[b + k] = new Vector2(i, 0);
                int t = i * 6;
                tris[t + 0] = b; tris[t + 1] = b + 2; tris[t + 2] = b + 1;
                tris[t + 3] = b; tris[t + 4] = b + 3; tris[t + 5] = b + 2;
            }
            mesh.vertices = verts;
            mesh.uv = uv;
            mesh.uv2 = uv2;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * boundsRadius * 2f);
            return mesh;
        }

        /// <summary>Outward UV sphere (proxy for ray-marched objects).</summary>
        public static Mesh Sphere(float radius, int lon = 32, int lat = 16)
        {
            var m = InwardSphere(radius, lon, lat);
            var tris = m.triangles;
            for (int i = 0; i < tris.Length; i += 3) { int t = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = t; }
            m.triangles = tris;
            var n = m.normals; for (int i = 0; i < n.Length; i++) n[i] = -n[i];
            m.normals = n;
            m.name = "Sphere";
            return m;
        }
    }
}
