/**
 * Bump the version suffix whenever PersonDto's shape changes: the key carries it, so an
 * old browser's copy simply misses and refetches instead of being trusted with fields it
 * predates. v2 added `email`, and a v1 copy without it would have rendered the profile
 * form with an empty address box and saved that emptiness back over the stored one.
 *
 * Its own module because @/auth clears it when identity changes and @/stores/person owns
 * it; importing the store from auth would close a cycle (the store imports @/auth back).
 */
export const PERSON_CACHE_KEY = "person.v2";
