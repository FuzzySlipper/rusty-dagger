//! Source-linked Product Kernel for the Privateer's Hold vertical slice.
//!
//! This is deliberately a normal Cargo library.  Product Assembly includes
//! this entrypoint directly, while the retained Dagger runtime and RPG closure
//! lives in sibling kernel-local crates rather than reaching back into the
//! old workspace.

#![forbid(unsafe_code)]

#[path = "src/adapter.rs"]
mod adapter;
#[path = "src/model.rs"]
mod model;
#[path = "src/planner.rs"]
mod planner;
#[path = "src/projection.rs"]
mod projection;
#[path = "src/resources.rs"]
mod resources;

pub use adapter::{DaggerProductAdapter, KernelError};
pub use model::DaggerProductAuthority;

use rusty_engine::{
    product_kernel::{
        ProductKernelRuntimeDefinition, ProductKernelRuntimeMutationDescriptor,
        ProductKernelRuntimeSelection, ProductRuntimeResources,
    },
    product_model::{
        CapabilityAccess, CapabilityAvailability, CapabilityBudget, CapabilityKind,
        CapabilityMetadata, CapabilityProvenance, CapabilityUses,
        ProductKernelCapabilityDescriptor,
    },
    runtime_standard_capabilities::BoundStandardCapabilities,
};

/// Fixed symbol consumed by generated Product Assembly source.
pub struct RustyProductRuntime;

const CAPABILITIES: &[ProductKernelCapabilityDescriptor] = &[
    ProductKernelCapabilityDescriptor::new(
        "dagger-move",
        CapabilityMetadata::new(
            CapabilityKind::Operation,
            CapabilityUses::INPUT_MAP,
            CapabilityAvailability::Linkable,
            CapabilityAccess::new(&[], &["dagger.session"]),
            CapabilityBudget::new(1_024),
            CapabilityProvenance::new("rusty-dagger.kernel", "kernel/entry.rs", "move"),
        ),
    ),
    ProductKernelCapabilityDescriptor::new(
        "dagger-look",
        CapabilityMetadata::new(
            CapabilityKind::Operation,
            CapabilityUses::INPUT_MAP,
            CapabilityAvailability::Linkable,
            CapabilityAccess::new(&[], &["dagger.session"]),
            CapabilityBudget::new(1_024),
            CapabilityProvenance::new("rusty-dagger.kernel", "kernel/entry.rs", "look"),
        ),
    ),
    ProductKernelCapabilityDescriptor::new(
        "dagger-attack",
        CapabilityMetadata::new(
            CapabilityKind::Operation,
            CapabilityUses::INPUT_MAP,
            CapabilityAvailability::Linkable,
            CapabilityAccess::new(&[], &["dagger.session", "dagger.combat"]),
            CapabilityBudget::new(1_024),
            CapabilityProvenance::new("rusty-dagger.kernel", "kernel/entry.rs", "attack"),
        ),
    ),
    ProductKernelCapabilityDescriptor::new(
        "dagger-session",
        CapabilityMetadata::new(
            CapabilityKind::Operation,
            CapabilityUses::INPUT_MAP,
            CapabilityAvailability::Linkable,
            CapabilityAccess::new(&[], &["dagger.session"]),
            CapabilityBudget::new(1_024),
            CapabilityProvenance::new("rusty-dagger.kernel", "kernel/entry.rs", "reset"),
        ),
    ),
    ProductKernelCapabilityDescriptor::new(
        "dagger-content",
        CapabilityMetadata::new(
            CapabilityKind::Operation,
            CapabilityUses::INPUT_MAP,
            CapabilityAvailability::Linkable,
            CapabilityAccess::new(&[], &["dagger.session"]),
            CapabilityBudget::new(1_024),
            CapabilityProvenance::new("rusty-dagger.kernel", "kernel/entry.rs", "jump_to_content"),
        ),
    ),
    ProductKernelCapabilityDescriptor::new(
        "dagger-equipment",
        CapabilityMetadata::new(
            CapabilityKind::Operation,
            CapabilityUses::INPUT_MAP,
            CapabilityAvailability::Linkable,
            CapabilityAccess::new(&[], &["dagger.inventory"]),
            CapabilityBudget::new(4_096),
            CapabilityProvenance::new("rusty-dagger.kernel", "kernel/entry.rs", "equipment"),
        ),
    ),
    ProductKernelCapabilityDescriptor::new(
        "dagger-inventory",
        CapabilityMetadata::new(
            CapabilityKind::Operation,
            CapabilityUses::INPUT_MAP,
            CapabilityAvailability::Linkable,
            CapabilityAccess::new(&[], &["dagger.inventory"]),
            CapabilityBudget::new(4_096),
            CapabilityProvenance::new("rusty-dagger.kernel", "kernel/entry.rs", "inventory"),
        ),
    ),
    ProductKernelCapabilityDescriptor::new(
        "dagger-loot",
        CapabilityMetadata::new(
            CapabilityKind::Operation,
            CapabilityUses::INPUT_MAP,
            CapabilityAvailability::Linkable,
            CapabilityAccess::new(&[], &["dagger.inventory", "dagger.loot"]),
            CapabilityBudget::new(4_096),
            CapabilityProvenance::new("rusty-dagger.kernel", "kernel/entry.rs", "loot"),
        ),
    ),
    ProductKernelCapabilityDescriptor::new(
        "dagger-settings",
        CapabilityMetadata::new(
            CapabilityKind::Operation,
            CapabilityUses::INPUT_MAP,
            CapabilityAvailability::Linkable,
            CapabilityAccess::new(&[], &["dagger.settings"]),
            CapabilityBudget::new(1_024),
            CapabilityProvenance::new("rusty-dagger.kernel", "kernel/entry.rs", "settings"),
        ),
    ),
    ProductKernelCapabilityDescriptor::new(
        "dagger-debug",
        CapabilityMetadata::new(
            CapabilityKind::Operation,
            CapabilityUses::INPUT_MAP,
            CapabilityAvailability::Linkable,
            CapabilityAccess::new(&[], &["dagger.debug"]),
            CapabilityBudget::new(1_024),
            CapabilityProvenance::new("rusty-dagger.kernel", "kernel/entry.rs", "debug_toggle"),
        ),
    ),
    ProductKernelCapabilityDescriptor::new(
        "dagger-simulation",
        CapabilityMetadata::new(
            CapabilityKind::System,
            CapabilityUses::SCHEDULE,
            CapabilityAvailability::Linkable,
            CapabilityAccess::new(&["dagger.session"], &["dagger.session"]),
            CapabilityBudget::new(1_024),
            CapabilityProvenance::new("rusty-dagger.kernel", "kernel/entry.rs", "simulation"),
        ),
    ),
    ProductKernelCapabilityDescriptor::new(
        "dagger-simulation-result",
        CapabilityMetadata::new(
            CapabilityKind::Operation,
            CapabilityUses::SCHEDULE,
            CapabilityAvailability::Linkable,
            CapabilityAccess::new(&["dagger.session"], &["dagger.session"]),
            CapabilityBudget::new(1_024),
            CapabilityProvenance::new(
                "rusty-dagger.kernel",
                "kernel/entry.rs",
                "simulation_result",
            ),
        ),
    ),
    ProductKernelCapabilityDescriptor::new(
        "dagger-observe-pairs-result",
        CapabilityMetadata::new(
            CapabilityKind::Operation,
            CapabilityUses::SCHEDULE,
            CapabilityAvailability::Linkable,
            CapabilityAccess::new(
                &[
                    "entity-state.components",
                    "entity-state.transforms",
                    "engine-spatial.occlusion",
                ],
                &["dagger.session"],
            ),
            CapabilityBudget::new(16_384),
            CapabilityProvenance::new(
                "rusty-dagger.kernel",
                "kernel/entry.rs",
                "observe_pairs_result",
            ),
        ),
    ),
];

const SELECTIONS: &[ProductKernelRuntimeSelection] = &[
    ProductKernelRuntimeSelection::new(
        "dagger-move",
        "kernel.dagger-move",
        "dagger.move.v1",
        CapabilityKind::Operation,
    ),
    ProductKernelRuntimeSelection::new(
        "dagger-look",
        "kernel.dagger-look",
        "dagger.look.v1",
        CapabilityKind::Operation,
    ),
    ProductKernelRuntimeSelection::new(
        "dagger-attack",
        "kernel.dagger-attack",
        "dagger.attack.v1",
        CapabilityKind::Operation,
    ),
    ProductKernelRuntimeSelection::new(
        "dagger-session",
        "kernel.dagger-session",
        "dagger.session.v1",
        CapabilityKind::Operation,
    ),
    ProductKernelRuntimeSelection::new(
        "dagger-content",
        "kernel.dagger-content",
        "dagger.content.jump.v1",
        CapabilityKind::Operation,
    ),
    ProductKernelRuntimeSelection::new(
        "dagger-equipment",
        "kernel.dagger-equipment",
        "dagger.equipment.v1",
        CapabilityKind::Operation,
    ),
    ProductKernelRuntimeSelection::new(
        "dagger-inventory",
        "kernel.dagger-inventory",
        "dagger.inventory.v1",
        CapabilityKind::Operation,
    ),
    ProductKernelRuntimeSelection::new(
        "dagger-loot",
        "kernel.dagger-loot",
        "dagger.loot.v1",
        CapabilityKind::Operation,
    ),
    ProductKernelRuntimeSelection::new(
        "dagger-settings",
        "kernel.dagger-settings",
        "dagger.settings.v1",
        CapabilityKind::Operation,
    ),
    ProductKernelRuntimeSelection::new(
        "dagger-debug",
        "kernel.dagger-debug",
        "dagger.debug.v1",
        CapabilityKind::Operation,
    ),
    ProductKernelRuntimeSelection::new(
        "dagger-simulation",
        "kernel.dagger-simulation",
        "dagger.simulation.v1",
        CapabilityKind::System,
    ),
    ProductKernelRuntimeSelection::new(
        "dagger-simulation-result",
        "kernel.dagger-simulation-result",
        "dagger.simulation.result.v1",
        CapabilityKind::Operation,
    ),
    ProductKernelRuntimeSelection::new(
        "dagger-observe-pairs-result",
        "kernel.dagger-observe-pairs-result",
        "engine.runtime.observe-pairs.result.v1",
        CapabilityKind::Operation,
    ),
];

const MUTATIONS: &[ProductKernelRuntimeMutationDescriptor] = &[
    ProductKernelRuntimeMutationDescriptor::new(
        "dagger.move",
        "kernel.dagger-move",
        "dagger.session",
        "dagger.runtime",
        "dagger.move.v1",
    ),
    ProductKernelRuntimeMutationDescriptor::new(
        "dagger.look",
        "kernel.dagger-look",
        "dagger.session",
        "dagger.runtime",
        "dagger.look.v1",
    ),
    ProductKernelRuntimeMutationDescriptor::new(
        "dagger.attack",
        "kernel.dagger-attack",
        "dagger.session",
        "dagger.runtime",
        "dagger.attack.v1",
    ),
    ProductKernelRuntimeMutationDescriptor::new(
        "dagger.session",
        "kernel.dagger-session",
        "dagger.session",
        "dagger.runtime",
        "dagger.session.v1",
    ),
    ProductKernelRuntimeMutationDescriptor::new(
        "dagger.content",
        "kernel.dagger-content",
        "dagger.session",
        "dagger.runtime",
        "dagger.content.jump.v1",
    ),
    ProductKernelRuntimeMutationDescriptor::new(
        "dagger.equipment",
        "kernel.dagger-equipment",
        "dagger.session",
        "dagger.runtime",
        "dagger.equipment.v1",
    ),
    ProductKernelRuntimeMutationDescriptor::new(
        "dagger.inventory",
        "kernel.dagger-inventory",
        "dagger.session",
        "dagger.runtime",
        "dagger.inventory.v1",
    ),
    ProductKernelRuntimeMutationDescriptor::new(
        "dagger.loot",
        "kernel.dagger-loot",
        "dagger.session",
        "dagger.runtime",
        "dagger.loot.v1",
    ),
    ProductKernelRuntimeMutationDescriptor::new(
        "dagger.settings",
        "kernel.dagger-settings",
        "dagger.session",
        "dagger.runtime",
        "dagger.settings.v1",
    ),
    ProductKernelRuntimeMutationDescriptor::new(
        "dagger.debug",
        "kernel.dagger-debug",
        "dagger.session",
        "dagger.runtime",
        "dagger.debug.v1",
    ),
    ProductKernelRuntimeMutationDescriptor::new(
        "dagger.simulation-result",
        "kernel.dagger-simulation-result",
        "dagger.session",
        "dagger.runtime",
        "dagger.simulation.result.v1",
    ),
    ProductKernelRuntimeMutationDescriptor::new(
        "dagger.observe-pairs-result",
        "kernel.dagger-observe-pairs-result",
        "dagger.session",
        "dagger.runtime",
        "engine.runtime.observe-pairs.result.v1",
    ),
];

impl ProductKernelRuntimeDefinition for RustyProductRuntime {
    type Adapter = DaggerProductAdapter;
    type Error = KernelError;
    type ProductState = DaggerProductAuthority;
    type ObserverComponent = ();
    type TargetComponent = ();

    fn capabilities() -> &'static [ProductKernelCapabilityDescriptor] {
        CAPABILITIES
    }
    fn selections() -> &'static [ProductKernelRuntimeSelection] {
        SELECTIONS
    }
    fn mutation_descriptors() -> &'static [ProductKernelRuntimeMutationDescriptor] {
        MUTATIONS
    }

    fn build(resources: ProductRuntimeResources<'_>) -> Result<Self::Adapter, Self::Error> {
        DaggerProductAdapter::from_resources(resources)
    }

    fn bind_standard_capabilities(
        adapter: &mut Self::Adapter,
        plans: BoundStandardCapabilities,
    ) -> Result<(), rusty_engine::product_kernel::ProductKernelStandardCapabilityBindError> {
        let observe_pairs = plans.into_observe_pairs();
        adapter.observe_pairs = observe_pairs;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use std::collections::BTreeSet;

    use super::{CAPABILITIES, MUTATIONS, SELECTIONS};
    use crate::DaggerProductAdapter;
    use rusty_engine::{
        product_model::{
            admit_checked_product_composition, decode_compiled_composition,
            decode_product_manifest, link_admitted_product_composition,
            validate_compiled_composition, ScheduleComposition,
        },
        runtime_composition::{RuntimeComposition, RuntimeCompositionInputs},
        runtime_input::{CompiledInputMappings, InputContext},
        runtime_lifecycle::{
            HostMonotonicTime, RealtimeLifecycleConfig, RuntimeInstanceId, RuntimeLifecycleConfig,
        },
        runtime_mutation::{CompiledMutationCatalog, MutationCapabilityDescriptor},
        runtime_schedule::CompiledRuntimeSchedule,
        runtime_timeline::CompiledTimelineCatalog,
    };

    const PROJECT: &[u8] =
        include_bytes!("dagger-runtime/tests/fixtures/privateers-hold.project.json");
    const NAVGRID: &[u8] =
        include_bytes!("dagger-runtime/tests/fixtures/privateers-hold.navgrid.json");
    const ENCOUNTERS: &[u8] =
        include_bytes!("dagger-runtime/tests/fixtures/privateers-hold.encounters.json");
    const GAMEPLAY: &[u8] =
        include_bytes!("dagger-runtime/tests/fixtures/dagger-core.package.json");

    fn simulation_only_composition() -> rusty_engine::product_model::LinkedProductComposition {
        let decoded =
            decode_compiled_composition(include_bytes!("../generated/compiled-composition.json"))
                .expect("generated Dagger composition");
        let mut candidate = decoded.candidate().clone();
        let ScheduleComposition::Append { systems } = &mut candidate.schedule[1].composition else {
            panic!("Dagger simulation phase is append-composed");
        };
        systems.retain(|system| system.id == "dagger.simulation");
        systems[0].after.clear();
        let checked =
            validate_compiled_composition(candidate).expect("simulation-only composition");
        let manifest =
            decode_product_manifest(include_str!("../rusty.toml")).expect("Dagger manifest");
        let admitted =
            admit_checked_product_composition(&manifest, checked).expect("admitted composition");
        link_admitted_product_composition(admitted, CAPABILITIES).expect("kernel linkage")
    }

    fn simulation_root() -> RuntimeComposition<DaggerProductAdapter> {
        let linked = simulation_only_composition();
        let input = CompiledInputMappings::compile(&linked).expect("input compilation");
        let schedule = CompiledRuntimeSchedule::compile(&linked).expect("schedule compilation");
        let timeline = CompiledTimelineCatalog::compile(&linked).expect("timeline compilation");
        let mutation_descriptors = MUTATIONS
            .iter()
            .map(|descriptor| {
                MutationCapabilityDescriptor::new(
                    descriptor.binding_id(),
                    descriptor.target(),
                    descriptor.publication_domain(),
                    descriptor.owner(),
                    descriptor.operation_type(),
                )
            })
            .collect::<Vec<_>>();
        let mutation = CompiledMutationCatalog::compile(&linked, &mutation_descriptors)
            .expect("simulation mutation catalog");
        let runtime = dagger_runtime::DaggerRuntime::from_product_resources(
            PROJECT, NAVGRID, ENCOUNTERS, GAMEPLAY,
        )
        .expect("admitted Dagger fixture runtime");
        let mut root = RuntimeComposition::new(
            RuntimeInstanceId::new(7266),
            RuntimeLifecycleConfig::Realtime(
                RealtimeLifecycleConfig::new(60, 4).expect("60 Hz lifecycle"),
            ),
            RuntimeCompositionInputs::new(
                input,
                schedule,
                timeline,
                mutation,
                InputContext::new("gameplay.default").expect("Dagger input context"),
            ),
            DaggerProductAdapter::from_runtime_for_test(runtime),
        );
        root.start().expect("start composition");
        root
    }

    #[test]
    fn selections_and_mutations_use_the_capability_local_targets() {
        for selection in SELECTIONS {
            let capability = CAPABILITIES
                .iter()
                .find(|capability| capability.identity() == selection.identity())
                .expect("every selection has its declared capability");
            assert_eq!(
                selection.target(),
                format!("kernel.{}", capability.identity()),
                "{} must use the Product Model local target",
                selection.identity()
            );
        }
        for mutation in MUTATIONS {
            assert!(
                SELECTIONS
                    .iter()
                    .any(|selection| selection.target() == mutation.target()),
                "{} queues only a declared local target",
                mutation.binding_id()
            );
        }
        let bindings = MUTATIONS
            .iter()
            .map(|mutation| mutation.binding_id())
            .collect::<BTreeSet<_>>();
        assert_eq!(
            bindings.len(),
            MUTATIONS.len(),
            "one descriptor per binding"
        );
        assert_eq!(
            bindings,
            BTreeSet::from([
                "dagger.move",
                "dagger.look",
                "dagger.attack",
                "dagger.session",
                "dagger.content",
                "dagger.equipment",
                "dagger.inventory",
                "dagger.loot",
                "dagger.settings",
                "dagger.debug",
                "dagger.simulation-result",
                "dagger.observe-pairs-result",
            ])
        );
    }

    #[test]
    fn scheduled_simulation_publishes_a_real_tick_through_the_sole_mutation_lane() {
        let mut root = simulation_root();
        let before = root
            .adapter()
            .runtime_for_test()
            .product_readout()
            .expect("initial authority readout");
        assert!(root
            .advance_realtime(HostMonotonicTime::from_nanoseconds(0))
            .expect("baseline realtime advance")
            .is_empty());
        let mut applied = 0;
        for tick in 1_u64..=60 {
            let steps = root
                .advance_realtime(HostMonotonicTime::from_nanoseconds(16_666_667 * tick))
                .expect("60 Hz realtime step");
            for step in steps {
                if let rusty_engine::runtime_composition::MutationStepReceipt::Applied(receipt) =
                    step.mutation
                {
                    assert_eq!(receipt.operations().len(), 1);
                    assert_eq!(
                        receipt.operations()[0].binding_id(),
                        "dagger.simulation-result"
                    );
                    assert_eq!(
                        receipt.operations()[0].target(),
                        "kernel.dagger-simulation-result"
                    );
                    applied += 1;
                }
            }
        }
        assert_eq!(
            applied, 60,
            "each due schedule invocation publishes one result operation"
        );
        let after = root
            .adapter()
            .runtime_for_test()
            .product_readout()
            .expect("published authority readout");
        assert_ne!(before.content, after.content);
    }
}
