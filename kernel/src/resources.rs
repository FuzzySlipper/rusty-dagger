use crate::adapter::KernelError;
use rusty_engine::product_kernel::ProductRuntimeResources;
pub struct DaggerRuntimeResources<'a> {
    pub project: &'a [u8],
    pub navgrid: &'a [u8],
    pub encounters: &'a [u8],
    pub gameplay_package: &'a [u8],
    pub dungeon_mesh: &'a [u8],
}
impl<'a> DaggerRuntimeResources<'a> {
    pub fn decode(r: ProductRuntimeResources<'a>) -> Result<Self, KernelError> {
        Ok(Self {
            project: need(r, "content/projects/privateers-hold.project.json")?,
            navgrid: need(r, "content/projects/privateers-hold.navgrid.json")?,
            encounters: need(r, "content/runtime/privateers-hold.encounters.json")?,
            gameplay_package: need(r, "content/runtime/dagger-core.package.json")?,
            dungeon_mesh: need(r, "content/meshes/privateers-hold.rmesh")?,
        })
    }
}
fn need<'a>(r: ProductRuntimeResources<'a>, p: &str) -> Result<&'a [u8], KernelError> {
    r.resource(p)
        .ok_or_else(|| KernelError::MissingResource(p.to_owned()))
}
