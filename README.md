# Babbys-first-line-of-sight-component
Component for Unity that creates a quick lookup system for calculating line of sight
## Use case
The Visibility Manager is a component that when given a set of geometry, generates a set of triangles which are later used for queries to determine whether visibility is possible or not. For most applications users will be better off using Raycast to quickly determine line of sight, however Visibility Manager can offer a vast improvement in performance if the following conditions are met:

1. Line of sight can be defined from a top down perspective (height is not a factor, all obstacles are infinitely tall).
2. The observer's location relative to their environment is static.
3. There are so many objects that need to have their line of sight tested that ray casting every frame is no longer feasible.

In these scenarios it is possible to repeatedly fall back on the pre-calculated values and exploit the fast calculation provided by Visibility Manager's lookup system without incurring the heavy cost establishing the system in the first place.

## Usage
### The parent object:
To create a Visibility Manager, the component must be attached to an empty game object. **The transform of this game object cannot be arbitrary**. Since the structure that Visibility manager creates is a set of triangles we need a boundary on our area. Through the game object, the boundary can be defined using the x and z properties of the transform scale. This boundary is a square if x == z.

### Creating obstacles:
Any objects that are the children of the parent object have their transforms used to define obstacles that are respected by the observer. As an object bounding algorithm is not implemented, all objects are assumed to be 1x1x1 cubes. **Do not use a mesh of a custom asset and expect everything to work**. Much like the parent object, the transform scale of these objects is what defines their dimensions. While the example uses meshes of cubes, only the transforms are used so meshes are not required in practice.

### Querying the Visibility Manager:
Querying the visibility manager is done by passing your target's position (A Vector3) to the TestVisibility visibility function. The output of this function is -1 if the target is obscured or the index of the triangle that the target resides in (used internally).
Typically code along these lines should suffice:

`visibilityManager.TestVisibility(transform.position) != -1`

Like most components your code can call the `GetComponent` function to get a reference to the visibility manager.

### Properties
**Observer:** A transform that denotes the position of the observer. The visibility polygon that is generated is based on what would be visible from the position defined by the given transform.

**Intersect Compute**: A computer shader that is used to calculate the large number of line intersection calculations required. The default (and currently only) shader that does this work is the LineIntersectComputeShader.

**Mesh Filter:** Renders the visibility polygon to a given mesh filter. Works best when mesh of meshfilter is set to "none". 
