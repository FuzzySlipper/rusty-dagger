/**
 * Equipment vocabulary: the classic paper-doll slots (donor
 * `ItemEnums.cs EquipSlots` — adopted, minus classic's two Unknown slots) and
 * the capacity metrics items cost against. Slot `allowedClassifications`
 * gate what may equip where; an empty list is unrestricted (jewelry,
 * clothes, cloaks).
 *
 * Hands model the donor's exclusivity: `right-hand` takes one- and
 * two-handed weapons, `left-hand` takes a one-handed weapon or a shield.
 * Two-handed weapons and shields share the `hands` exclusivity group at
 * compile time, so a two-hander blocks a shield and vice versa while
 * dual-wielding two either-hand weapons stays legal.
 *
 * Capacity: the `weight` metric is counted in the classic quarter-kg unit;
 * an actor's limit is its `max-encumbrance` derived rule (kg) times four.
 */

import {
  equipmentSection,
  equipmentSlot,
  type EquipmentSection,
} from "../authoring/mod.js";

export const equipment: EquipmentSection = equipmentSection(
  ["weight"],
  [
    equipmentSlot("amulet0", []),
    equipmentSlot("amulet1", []),
    equipmentSlot("bracelet0", []),
    equipmentSlot("bracelet1", []),
    equipmentSlot("ring0", []),
    equipmentSlot("ring1", []),
    equipmentSlot("bracer0", []),
    equipmentSlot("bracer1", []),
    equipmentSlot("mark0", []),
    equipmentSlot("mark1", []),
    equipmentSlot("crystal0", []),
    equipmentSlot("crystal1", []),
    equipmentSlot("head", ["armor-head"]),
    equipmentSlot("right-arm", ["armor-right-arm"]),
    equipmentSlot("cloak1", []),
    equipmentSlot("left-arm", ["armor-left-arm"]),
    equipmentSlot("cloak2", []),
    equipmentSlot("chest-clothes", []),
    equipmentSlot("chest-armor", ["armor-chest"]),
    equipmentSlot("right-hand", ["weapon-one-hand", "weapon-two-hand"]),
    equipmentSlot("gloves", ["armor-hands"]),
    equipmentSlot("left-hand", ["weapon-one-hand", "shield"]),
    equipmentSlot("legs-armor", ["armor-legs"]),
    equipmentSlot("legs-clothes", []),
    equipmentSlot("feet", ["armor-feet"]),
  ],
);
